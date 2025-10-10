using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace BotCore.Services;

/// <summary>
/// Security service for token redaction and VPN/VPS/remote detection
/// Implements security guardrails to prevent trading from unauthorized environments
/// </summary>
public interface ISecurityService
{
    string RedactSensitiveData(string input);
    bool IsRemoteSessionDetected();
    bool IsTradingAllowed();
    void LogSecurityEvent(string eventType, object eventData);
}

public class SecurityService : ISecurityService
{
    private readonly ILogger<SecurityService> _logger;
    private readonly List<Regex> _sensitivePatterns;
    private readonly bool _allowRemote;

    public SecurityService(ILogger<SecurityService> logger)
    {
        _logger = logger;
        _allowRemote = Environment.GetEnvironmentVariable("ALLOW_REMOTE") == "1";
        
        // Initialize sensitive data patterns for redaction
        _sensitivePatterns = new List<Regex>
        {
            // Tokens and keys
            new Regex(@"(token|key|secret|password|auth|jwt|bearer)\s*[:=]\s*[""']?([a-zA-Z0-9\.\-_+/=]{8,})[""']?", RegexOptions.IgnoreCase),
            new Regex(@"(bearer\s+)([a-zA-Z0-9\.\-_+/=]{20,})", RegexOptions.IgnoreCase),
            
            // Account numbers and IDs
            new Regex(@"(\d{4})(\d{4,})(\d{4})", RegexOptions.IgnoreCase),
            
            // Credit card-like patterns
            new Regex(@"(\d{4}[\s\-]?\d{4}[\s\-]?\d{4}[\s\-]?\d{4})", RegexOptions.IgnoreCase),
            
            // Common sensitive headers
            new Regex(@"(Authorization|X-Auth-Token|X-API-Key)\s*:\s*(.+)", RegexOptions.IgnoreCase),
            
            // Email-like patterns in logs
            new Regex(@"(\w+)@(\w+\.\w+)", RegexOptions.IgnoreCase)
        };

        _logger.LogInformation("SecurityService initialized. Remote trading allowed: {AllowRemote}", _allowRemote);
    }

    public string RedactSensitiveData(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var result = input;

        foreach (var pattern in _sensitivePatterns)
        {
            try
            {
                if (pattern.ToString().Contains("bearer", StringComparison.OrdinalIgnoreCase))
                {
                    result = pattern.Replace(result, "$1[REDACTED]");
                }
                else if (pattern.ToString().Contains(@"(\d{4})(\d{4,})(\d{4})", StringComparison.Ordinal))
                {
                    result = pattern.Replace(result, "$1****$3");
                }
                else if (pattern.ToString().Contains("@", StringComparison.Ordinal))
                {
                    result = pattern.Replace(result, "$1@[REDACTED]");
                }
                else if (pattern.ToString().Contains("Authorization", StringComparison.Ordinal))
                {
                    result = pattern.Replace(result, "$1: [REDACTED]");
                }
                else
                {
                    result = pattern.Replace(result, "$1[REDACTED]");
                }
            }
            catch (Exception ex)
            {
                // Don't let redaction fail the operation
                _logger.LogDebug(ex, "Error applying redaction pattern");
            }
        }

        return result;
    }

    public bool IsRemoteSessionDetected()
    {
        try
        {
            var detectionResults = new List<(string Check, bool Detected, string Details)>();

            // Check for RDP session
            var rdpDetected = CheckRDPSession();
            detectionResults.Add(("RDP", rdpDetected.IsRemote, rdpDetected.Details));

            // Check for VPN adapters
            var vpnDetected = CheckVPNAdapters();
            detectionResults.Add(("VPN", vpnDetected.IsRemote, vpnDetected.Details));

            // Check for VM indicators
            var vmDetected = CheckVMIndicators();
            detectionResults.Add(("VM", vmDetected.IsRemote, vmDetected.Details));

            // Check environment variables for remote indicators
            var envDetected = CheckEnvironmentIndicators();
            detectionResults.Add(("ENV", envDetected.IsRemote, envDetected.Details));

            var anyRemoteDetected = detectionResults.Exists(r => r.Detected);

            // Log detection results
            var detectionData = new
            {
                timestamp = DateTime.UtcNow,
                component = "security_service",
                operation = "remote_detection",
                remote_detected = anyRemoteDetected,
                allow_remote = _allowRemote,
                checks = detectionResults.Select(r => new 
                { 
                    check = r.Check, 
                    detected = r.Detected, 
                    details = r.Details 
                }).ToArray()
            };

            _logger.LogInformation("REMOTE_DETECTION: {DetectionData}", 
                JsonSerializer.Serialize(detectionData));

            return anyRemoteDetected;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during remote session detection");
            return true; // Fail safe - assume remote if detection fails
        }
    }

    public bool IsTradingAllowed()
    {
        var isRemote = IsRemoteSessionDetected();
        var tradingAllowed = !isRemote || _allowRemote;

        if (!tradingAllowed)
        {
            var blockData = new
            {
                timestamp = DateTime.UtcNow,
                component = "security_service",
                operation = "trading_blocked",
                reason = "remote_session_detected",
                allow_remote = _allowRemote,
                recommendation = "Set ALLOW_REMOTE=1 to override (not recommended for production)"
            };

            _logger.LogWarning("TRADING_BLOCKED: {BlockData}", 
                JsonSerializer.Serialize(blockData));
        }

        return tradingAllowed;
    }

    public void LogSecurityEvent(string eventType, object eventData)
    {
        try
        {
            var redactedData = RedactSensitiveData(JsonSerializer.Serialize(eventData));
            
            var securityEvent = new
            {
                timestamp = DateTime.UtcNow,
                component = "security_service",
                event_type = eventType,
                data = JsonDocument.Parse(redactedData).RootElement
            };

            _logger.LogWarning("SECURITY_EVENT: {SecurityEvent}", 
                JsonSerializer.Serialize(securityEvent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log security event: {EventType}", eventType);
        }
    }

    private static (bool IsRemote, string Details) CheckRDPSession()
    {
        try
        {
            // Check session name environment variable
            var sessionName = Environment.GetEnvironmentVariable("SESSIONNAME");
            if (!string.IsNullOrEmpty(sessionName) && 
                sessionName.StartsWith("RDP-", StringComparison.OrdinalIgnoreCase))
            {
                return (true, $"RDP session detected: {sessionName}");
            }

            // Check for Terminal Services process
            var processes = Process.GetProcessesByName("winlogon");
            if (processes.Length > 1)
            {
                return (true, "Multiple winlogon processes detected (Terminal Services)");
            }

            // Check system metrics for RDP indicators
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                // Check for remote desktop connections via WMI would go here
                // For now, use basic checks
                var userInteractive = Environment.UserInteractive;
                if (!userInteractive)
                {
                    return (true, "Non-interactive session detected");
                }
            }

            return (false, "No RDP session detected");
        }
        catch (Exception ex)
        {
            return (true, $"RDP check failed: {ex.Message}");
        }
    }

    private static (bool IsRemote, string Details) CheckVPNAdapters()
    {
        try
        {
            var vpnIndicators = new[]
            {
                "VPN", "TUNNEL", "TAP", "TUN", "OPENVPN", "NORDVPN", "EXPRESSVPN",
                "WIREGUARD", "PPTP", "L2TP", "IPSEC", "CISCO", "CHECKPOINT"
            };

            var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
            var suspiciousAdapters = new List<string>();

            foreach (var adapter in networkInterfaces)
            {
                var name = adapter.Name.ToUpperInvariant();
                var description = adapter.Description.ToUpperInvariant();

                if (vpnIndicators.Exists(indicator => name.Contains(indicator, StringComparison.Ordinal) || description.Contains(indicator, StringComparison.Ordinal)))
                {
                    suspiciousAdapters.Add($"{adapter.Name} ({adapter.Description})");
                }
            }

            if (suspiciousAdapters.Count > 0)
            {
                return (true, $"VPN adapters detected: {string.Join(", ", suspiciousAdapters)}");
            }

            return (false, "No VPN adapters detected");
        }
        catch (Exception ex)
        {
            return (true, $"VPN check failed: {ex.Message}");
        }
    }

    private static (bool IsRemote, string Details) CheckVMIndicators()
    {
        try
        {
            var vmIndicators = new List<string>();

            // Check for common VM/VPS indicators
            var computerName = Environment.MachineName.ToUpperInvariant();
            var vmNames = new[] { "VM", "VPS", "CLOUD", "AWS", "AZURE", "GCP", "DIGITALOCEAN", "VULTR" };
            
            if (Array.Exists(vmNames, vm => computerName.Contains(vm, StringComparison.Ordinal)))
            {
                vmIndicators.Add($"VM-like computer name: {computerName}");
            }

            // Check for virtualization process indicators
            var vmProcesses = new[] { "VMWARE", "VBOX", "VIRTUALBOX", "QEMU", "HYPERV", "XEN" };
            var runningProcesses = Process.GetProcesses();
            
            foreach (var processName in runningProcesses.Select(process => process.ProcessName))
            {
                try
                {
                    var processNameUpper = processName.ToUpperInvariant();
                    if (Array.Exists(vmProcesses, vm => processNameUpper.Contains(vm, StringComparison.Ordinal)))
                    {
                        vmIndicators.Add($"VM process detected: {processName}");
                    }
                }
                catch
                {
                    // Ignore access denied errors
                }
            }

            // Check system directory for VM indicators (Windows)
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.System);
                var vmFiles = new[] { "vmware", "vbox", "virtualbox", "qemu" };
                
                foreach (var vmFile in vmFiles)
                {
                    var searchPattern = $"*{vmFile}*";
                    try
                    {
                        var files = Directory.GetFiles(systemRoot, searchPattern);
                        if (files.Length > 0)
                        {
                            vmIndicators.Add($"VM files found: {vmFile}");
                        }
                    }
                    catch
                    {
                        // Ignore access denied errors
                    }
                }
            }

            if (vmIndicators.Count > 0)
            {
                return (true, string.Join("; ", vmIndicators));
            }

            return (false, "No VM indicators detected");
        }
        catch (Exception ex)
        {
            return (true, $"VM check failed: {ex.Message}");
        }
    }

    private static (bool IsRemote, string Details) CheckEnvironmentIndicators()
    {
        try
        {
            var remoteIndicators = new List<string>();

            // Check common cloud/remote environment variables
            var remoteEnvVars = new[]
            {
                "AWS_REGION", "AZURE_CLIENT_ID", "GOOGLE_CLOUD_PROJECT",
                "KUBERNETES_SERVICE_HOST", "DOCKER_CONTAINER",
                "GITHUB_ACTIONS", "JENKINS_URL", "CI", "BUILD_ID"
            };

            foreach (var envVar in remoteEnvVars)
            {
                var value = Environment.GetEnvironmentVariable(envVar);
                if (!string.IsNullOrEmpty(value))
                {
                    remoteIndicators.Add($"{envVar} set");
                }
            }

            // Check user domain for enterprise/remote indicators
            var userDomain = Environment.UserDomainName;
            var remoteDomains = new[] { "AWS", "AZURE", "GCP", "CLOUD", "VPS" };
            
            if (Array.Exists(remoteDomains, domain => userDomain.ToUpperInvariant().Contains(domain, StringComparison.Ordinal)))
            {
                remoteIndicators.Add($"Remote domain: {userDomain}");
            }

            if (remoteIndicators.Count > 0)
            {
                return (true, string.Join("; ", remoteIndicators));
            }

            return (false, "No environment indicators detected");
        }
        catch (Exception ex)
        {
            return (true, $"Environment check failed: {ex.Message}");
        }
    }
}