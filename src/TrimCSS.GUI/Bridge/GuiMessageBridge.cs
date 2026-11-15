using System.Text.Json;
using InfiniFrame;
using TrimCSS.Contracts;
using TrimCSS.Engine;
using TrimCSS.Models;

namespace TrimCSS.GUI.Bridge;

/// <summary>
/// Represents an incoming request sent from the web front-end to the .NET desktop host.
/// </summary>
public class BridgeRequest
{
    /// <summary>
    /// Gets or sets the action to be performed (e.g., "selectFolder", "scanProject").
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the JSON payload containing arguments for the requested action.
    /// </summary>
    public JsonElement Payload { get; set; }
}

/// <summary>
/// Represents an outgoing response sent from the .NET desktop host to the web front-end.
/// </summary>
public class BridgeResponse
{
    /// <summary>
    /// Gets or sets the action corresponding to this response.
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the requested action succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the data returned by the action, if successful.
    /// </summary>
    public object? Data { get; set; }

    /// <summary>
    /// Gets or sets the error message, if the action failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Bridges web front-end IPC messages with TrimCSS scanning engine and native window capabilities.
/// </summary>
public class GuiMessageBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly ICssScanner _scanner;

    /// <summary>
    /// Initializes a new instance of the <see cref="GuiMessageBridge"/> class.
    /// </summary>
    /// <param name="scanner">Optional CSS scanner instance to use for scanning operations. Defaults to <see cref="ProjectScanner"/>.</param>
    public GuiMessageBridge(ICssScanner? scanner = null)
    {
        _scanner = scanner ?? new ProjectScanner();
    }

    /// <summary>
    /// Registers this bridge's web message receiver on the specified InfiniFrame window.
    /// </summary>
    /// <param name="window">The window on which to register web message handlers.</param>
    public void Register(IInfiniFrameWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window is InfiniFrameWindow concreteWindow)
        {
            concreteWindow.EventsStore.WebMessageReceived.Add(async (win, e) =>
            {
                await HandleMessageAsync(win, e.Message);
            });
        }
        else
        {
            var eventsStoreProp = window.GetType().GetProperty("EventsStore");
            var eventsStore = eventsStoreProp?.GetValue(window);
            var webMessageProp = eventsStore?.GetType().GetProperty("WebMessageReceived");
            var addMethod = webMessageProp?.PropertyType.GetMethod("Add");
            addMethod?.Invoke(webMessageProp?.GetValue(eventsStore), [
                new Action<IInfiniFrameWindow, InfiniFrameWebMessageReceivedEvent>(async (win, e) =>
                {
                    await HandleMessageAsync(win, e.Message);
                })
            ]);
        }
    }

    /// <summary>
    /// Registers this bridge's web message receiver on the specified window builder before building.
    /// </summary>
    /// <param name="builder">The window builder on which to register web message handlers.</param>
    public void Register(InfiniFrameWindowBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.EventsStore.WebMessageReceived.Add(async (win, e) =>
        {
            await HandleMessageAsync(win, e.Message);
        });
    }

    /// <summary>
    /// Handles an incoming raw JSON message from the web front-end and dispatches the corresponding action.
    /// </summary>
    /// <param name="window">The InfiniFrame window context for responding.</param>
    /// <param name="rawMessage">The raw JSON string received from the web page.</param>
    public async Task HandleMessageAsync(IInfiniFrameWindow window, string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return;
        }

        BridgeRequest? request = null;
        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(rawMessage, JsonOptions);
            if (request == null || string.IsNullOrWhiteSpace(request.Action))
            {
                SendError(window, "unknown", "Invalid or missing action in request.");
                return;
            }

            switch (request.Action)
            {
                case "selectFolder":
                    await HandleSelectFolderAsync(window);
                    break;

                case "scanProject":
                    var response = await ProcessScanProjectAsync(request.Payload);
                    SendResponse(window, response);
                    break;

                default:
                    SendError(window, request.Action, $"Unknown action '{request.Action}'.");
                    break;
            }
        }
        catch (Exception ex)
        {
            SendError(window, request?.Action ?? "unknown", ex.Message);
        }
    }

    /// <summary>
    /// Directly processes a request object, enabling headless testing of bridge business logic.
    /// </summary>
    /// <param name="request">The request to process.</param>
    /// <returns>A task that resolves to the corresponding <see cref="BridgeResponse"/>.</returns>
    public async Task<BridgeResponse> ProcessRequestAsync(BridgeRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Action))
        {
            return new BridgeResponse
            {
                Action = request?.Action ?? "unknown",
                Success = false,
                ErrorMessage = "Invalid or missing action in request."
            };
        }

        return request.Action switch
        {
            "scanProject" => await ProcessScanProjectAsync(request.Payload),
            _ => new BridgeResponse
            {
                Action = request.Action,
                Success = false,
                ErrorMessage = $"Unknown action '{request.Action}'."
            }
        };
    }

    private async Task HandleSelectFolderAsync(IInfiniFrameWindow window)
    {
        string? selectedFolder = null;

        try
        {
            if (window.Features.FilePickerDialogs != null)
            {
                var paths = await window.Features.FilePickerDialogs.ShowOpenFolderAsync(
                    title: "Select Web Project Folder",
                    defaultPath: string.Empty,
                    multiSelect: false,
                    ct: CancellationToken.None);

                selectedFolder = paths?.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            SendError(window, "selectFolder", $"Failed to open folder picker: {ex.Message}");
            return;
        }

        SendSuccess(window, "selectFolder", new { folderPath = selectedFolder });
    }

    private async Task<BridgeResponse> ProcessScanProjectAsync(JsonElement payload)
    {
        string? folderPath = null;

        if (payload.ValueKind == JsonValueKind.String)
        {
            folderPath = payload.GetString();
        }
        else if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("folderPath", out var prop))
        {
            folderPath = prop.GetString();
        }

        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return new BridgeResponse
            {
                Action = "scanProject",
                Success = false,
                ErrorMessage = "A valid folder path must be provided."
            };
        }

        if (!Directory.Exists(folderPath))
        {
            return new BridgeResponse
            {
                Action = "scanProject",
                Success = false,
                ErrorMessage = $"Directory '{folderPath}' does not exist."
            };
        }

        try
        {
            // Run project scan on a background worker thread so the UI thread is never blocked
            ScanResult result = await Task.Run(async () =>
            {
                return await _scanner.ScanProjectAsync(folderPath);
            });

            return new BridgeResponse
            {
                Action = "scanProject",
                Success = true,
                Data = result
            };
        }
        catch (Exception ex)
        {
            return new BridgeResponse
            {
                Action = "scanProject",
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private static void SendSuccess(IInfiniFrameWindow window, string action, object? data)
    {
        var response = new BridgeResponse
        {
            Action = action,
            Success = true,
            Data = data,
            ErrorMessage = null
        };

        SendResponse(window, response);
    }

    private static void SendError(IInfiniFrameWindow window, string action, string errorMessage)
    {
        var response = new BridgeResponse
        {
            Action = action,
            Success = false,
            Data = null,
            ErrorMessage = errorMessage
        };

        SendResponse(window, response);
    }

    private static void SendResponse(IInfiniFrameWindow window, BridgeResponse response)
    {
        try
        {
            string json = JsonSerializer.Serialize(response, JsonOptions);
            window.Features.WebMessaging?.SendWebMessage(json);
        }
        catch
        {
            // Suppress transport errors if window is closing
        }
    }
}
