using System.Text.Json;
using TrimCSS.Contracts;
using TrimCSS.GUI.Bridge;
using TrimCSS.Models;
using Xunit;

namespace TrimCSS.Tests;

public class GuiMessageBridgeTests
{
    private readonly MockScanner _mockScanner = new();
    private readonly GuiMessageBridge _bridge;

    public GuiMessageBridgeTests()
    {
        _bridge = new GuiMessageBridge(_mockScanner);
    }

    [Fact]
    public async Task ProcessRequestAsync_ScanProject_WithValidDirectory_ReturnsSuccessAndScanResult()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"bridge_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var payloadJson = JsonSerializer.SerializeToElement(new { folderPath = tempDir });
            var request = new BridgeRequest
            {
                Action = "scanProject",
                Payload = payloadJson
            };

            // Act
            var response = await _bridge.ProcessRequestAsync(request);

            // Assert
            Assert.True(response.Success);
            Assert.Equal("scanProject", response.Action);
            Assert.Null(response.ErrorMessage);
            Assert.NotNull(response.Data);

            var result = Assert.IsType<ScanResult>(response.Data);
            Assert.Equal(42, result.TotalCssSelectors);
            Assert.Equal(5, result.ScannedFilesCount);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task ProcessRequestAsync_ScanProject_WithStringPayload_ReturnsSuccess()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"bridge_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var payloadJson = JsonSerializer.SerializeToElement(tempDir);
            var request = new BridgeRequest
            {
                Action = "scanProject",
                Payload = payloadJson
            };

            // Act
            var response = await _bridge.ProcessRequestAsync(request);

            // Assert
            Assert.True(response.Success);
            Assert.NotNull(response.Data);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task ProcessRequestAsync_ScanProject_WithNonExistentDirectory_ReturnsError()
    {
        // Arrange
        var nonExistent = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}");
        var payloadJson = JsonSerializer.SerializeToElement(new { folderPath = nonExistent });
        var request = new BridgeRequest
        {
            Action = "scanProject",
            Payload = payloadJson
        };

        // Act
        var response = await _bridge.ProcessRequestAsync(request);

        // Assert
        Assert.False(response.Success);
        Assert.Equal("scanProject", response.Action);
        Assert.Contains("does not exist", response.ErrorMessage);
    }

    [Fact]
    public async Task ProcessRequestAsync_ScanProject_WithEmptyPath_ReturnsError()
    {
        // Arrange
        var payloadJson = JsonSerializer.SerializeToElement(new { folderPath = "" });
        var request = new BridgeRequest
        {
            Action = "scanProject",
            Payload = payloadJson
        };

        // Act
        var response = await _bridge.ProcessRequestAsync(request);

        // Assert
        Assert.False(response.Success);
        Assert.Equal("scanProject", response.Action);
        Assert.Contains("valid folder path", response.ErrorMessage);
    }

    [Fact]
    public async Task ProcessRequestAsync_WithUnknownAction_ReturnsError()
    {
        // Arrange
        var request = new BridgeRequest
        {
            Action = "unsupportedAction",
            Payload = default
        };

        // Act
        var response = await _bridge.ProcessRequestAsync(request);

        // Assert
        Assert.False(response.Success);
        Assert.Equal("unsupportedAction", response.Action);
        Assert.Contains("Unknown action", response.ErrorMessage);
    }

    [Fact]
    public void BridgeResponse_SerializesWithCamelCaseNaming()
    {
        // Arrange
        var response = new BridgeResponse
        {
            Action = "testAction",
            Success = true,
            Data = new { totalSelectors = 10 },
            ErrorMessage = null
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Act
        var json = JsonSerializer.Serialize(response, options);

        // Assert
        Assert.Contains("\"action\":\"testAction\"", json);
        Assert.Contains("\"success\":true", json);
        Assert.Contains("\"errorMessage\":null", json);
    }

    private class MockScanner : ICssScanner
    {
        public Task<ScanResult> ScanProjectAsync(string projectFolderPath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ScanResult
            {
                TotalCssSelectors = 42,
                ScannedFilesCount = 5,
                ExecutionTime = TimeSpan.FromMilliseconds(150),
                UnusedSelectors = [],
                AmbiguousSelectors = []
            });
        }
    }
}
