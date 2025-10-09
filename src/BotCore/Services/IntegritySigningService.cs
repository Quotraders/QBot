using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TradingBot.BotCore.Services
{
    /// <summary>
    /// Integrity and signing service for logs, models, and manifests
    /// Provides SHA-256 hashing and digital signing for production data integrity
    /// </summary>
    public class IntegritySigningService
    {
        private readonly ILogger<IntegritySigningService> _logger;
        private readonly RSA _signingKey;
        private readonly string _publicKeyPem;
        private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

        // Cryptographic configuration constants
        private const int RsaKeySizeBits = 2048;              // RSA key size for signing
        private const int HashPrefixLength = 12;             // Length of hash prefix for logging

        public IntegritySigningService(ILogger<IntegritySigningService> logger)
        {
            _logger = logger;
            
            // Initialize RSA key pair for signing (in production, load from secure key store)
            _signingKey = RSA.Create(RsaKeySizeBits);
            _publicKeyPem = Convert.ToBase64String(_signingKey.ExportRSAPublicKey());
            
            _logger.LogInformation("🔐 [INTEGRITY] Integrity signing service initialized with RSA-2048");
        }

        /// <summary>
        /// Calculate SHA-256 hash of a file
        /// </summary>
        public async Task<string> CalculateFileHashAsync(string filePath)
        {
            try
            {
                using var sha256 = SHA256.Create();
                using var fileStream = File.OpenRead(filePath);
                var hashBytes = await Task.Run(() => sha256.ComputeHash(fileStream)).ConfigureAwait(false);
                var hash = Convert.ToHexString(hashBytes).ToUpperInvariant();
                
                _logger.LogDebug("📋 [INTEGRITY] File hash calculated: {File} -> {Hash}", 
                    Path.GetFileName(filePath), hash[..HashPrefixLength] + "...");
                    
                return hash;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🚨 [INTEGRITY] Error calculating file hash: {File}", filePath);
                throw new InvalidOperationException($"File hash calculation failed for {filePath}", ex);
            }
        }

        /// <summary>
        /// Calculate SHA-256 hash of string content
        /// </summary>
        public string CalculateContentHash(string content)
        {
            try
            {
                var contentBytes = Encoding.UTF8.GetBytes(content);
                var hashBytes = SHA256.HashData(contentBytes);
                return Convert.ToHexString(hashBytes).ToUpperInvariant();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🚨 [INTEGRITY] Error calculating content hash");
                throw new InvalidOperationException("Failed to calculate SHA256 hash for content integrity verification", ex);
            }
        }

        /// <summary>
        /// Create a signed manifest for a set of files
        /// </summary>
        public async Task<SignedManifest> CreateSignedManifestAsync(string manifestName, IEnumerable<string> filePaths)
        {
            ArgumentNullException.ThrowIfNull(manifestName);
            ArgumentNullException.ThrowIfNull(filePaths);
            
            try
            {
                var manifest = new SignedManifest
                {
                    Name = manifestName,
                    CreatedAt = DateTime.UtcNow,
                    Version = "1.0"
                };

                // Calculate hash for each file
                foreach (var filePath in filePaths)
                {
                    if (File.Exists(filePath))
                    {
                        var fileInfo = new FileInfo(filePath);
                        var hash = await CalculateFileHashAsync(filePath).ConfigureAwait(false);
                        
                        var fileIntegrity = new FileIntegrity
                        {
                            Path = filePath,
                            Hash = hash,
                            Size = fileInfo.Length,
                            LastModified = fileInfo.LastWriteTimeUtc,
                            HashAlgorithm = "SHA256"
                        };
                        manifest.SetFile(Path.GetFileName(filePath), fileIntegrity);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ [INTEGRITY] File not found for manifest: {File}", filePath);
                    }
                }

                // Create manifest content
                var manifestJson = JsonSerializer.Serialize(manifest, s_jsonOptions);
                manifest.ContentHash = CalculateContentHash(manifestJson);
                
                // Sign the manifest
                manifest.Signature = SignContent(manifestJson);
                manifest.PublicKey = _publicKeyPem;

                _logger.LogInformation("✅ [INTEGRITY] Created signed manifest '{Name}' with {Count} files", 
                    manifestName, manifest.Files.Count);

                return manifest;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🚨 [INTEGRITY] Error creating signed manifest: {Name}", manifestName);
                throw new InvalidOperationException($"Failed to create signed manifest '{manifestName}' with integrity verification", ex);
            }
        }

        /// <summary>
        /// Verify a signed manifest
        /// </summary>
        public async Task<ManifestVerificationResult> VerifySignedManifestAsync(SignedManifest manifest)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            
            var result = new ManifestVerificationResult
            {
                IsValid = false,
                VerifiedAt = DateTime.UtcNow
            };

            try
            {
                // Reconstruct manifest content for signature verification
                var tempManifest = new SignedManifest
                {
                    Name = manifest.Name,
                    CreatedAt = manifest.CreatedAt,
                    Version = manifest.Version,
                    ContentHash = manifest.ContentHash
                };
                
                // Copy files from original manifest
                tempManifest.ReplaceFiles(manifest.Files);

                var manifestJson = JsonSerializer.Serialize(tempManifest, s_jsonOptions);

                // Verify signature
                if (!VerifySignature(manifestJson, manifest.Signature, manifest.PublicKey))
                {
                    result.AddError("Invalid manifest signature");
                    _logger.LogError("🚨 [INTEGRITY] Invalid signature for manifest: {Name}", manifest.Name);
                    return result;
                }

                // Verify content hash
                var expectedHash = CalculateContentHash(manifestJson);
                if (manifest.ContentHash != expectedHash)
                {
                    result.AddError("Manifest content hash mismatch");
                    _logger.LogError("🚨 [INTEGRITY] Content hash mismatch for manifest: {Name}", manifest.Name);
                    return result;
                }

                // Verify individual file hashes
                var verifiedFiles = 0;
                var missingFiles = 0;
                var corruptFiles = 0;

                foreach (var fileEntry in manifest.Files)
                {
                    var filePath = fileEntry.Value.Path;
                    
                    if (!File.Exists(filePath))
                    {
                        missingFiles++;
                        result.AddError($"Missing file: {fileEntry.Key}");
                        continue;
                    }

                    var currentHash = await CalculateFileHashAsync(filePath).ConfigureAwait(false);
                    if (currentHash != fileEntry.Value.Hash)
                    {
                        corruptFiles++;
                        result.AddError($"File integrity violation: {fileEntry.Key}");
                        _logger.LogError("🚨 [INTEGRITY] File hash mismatch: {File}", fileEntry.Key);
                        continue;
                    }

                    verifiedFiles++;
                }

                result.VerifiedFiles = verifiedFiles;
                result.MissingFiles = missingFiles;
                result.CorruptFiles = corruptFiles;
                result.IsValid = result.Errors.Count == 0;

                if (result.IsValid)
                {
                    _logger.LogInformation("✅ [INTEGRITY] Manifest verification successful: {Name} ({Count} files)", 
                        manifest.Name, verifiedFiles);
                }
                else
                {
                    _logger.LogError("🚨 [INTEGRITY] Manifest verification failed: {Name} ({Errors} errors)", 
                        manifest.Name, result.Errors.Count);
                }

                return result;
            }
            catch (FileNotFoundException ex)
            {
                result.AddError($"Verification error: {ex.Message}");
                _logger.LogError(ex, "🚨 [INTEGRITY] Error verifying manifest: {Name}", manifest.Name);
                return result;
            }
            catch (DirectoryNotFoundException ex)
            {
                result.AddError($"Verification error: {ex.Message}");
                _logger.LogError(ex, "🚨 [INTEGRITY] Error verifying manifest: {Name}", manifest.Name);
                return result;
            }
            catch (UnauthorizedAccessException ex)
            {
                result.AddError($"Verification error: {ex.Message}");
                _logger.LogError(ex, "🚨 [INTEGRITY] Error verifying manifest: {Name}", manifest.Name);
                return result;
            }
            catch (IOException ex)
            {
                result.AddError($"Verification error: {ex.Message}");
                _logger.LogError(ex, "🚨 [INTEGRITY] Error verifying manifest: {Name}", manifest.Name);
                return result;
            }
        }

        /// <summary>
        /// Sign trading logs with integrity guarantee
        /// </summary>
        public Task<SignedLogEntry> SignLogEntryAsync(string logContent, string logSource)
        {
            try
            {
                var entry = new SignedLogEntry
                {
                    Content = logContent,
                    Source = logSource,
                    Timestamp = DateTime.UtcNow,
                    ContentHash = CalculateContentHash(logContent)
                };

                var entryJson = JsonSerializer.Serialize(entry, s_jsonOptions);
                entry.Signature = SignContent(entryJson);
                entry.PublicKey = _publicKeyPem;

                _logger.LogDebug("📝 [INTEGRITY] Signed log entry from {Source}", logSource);
                return Task.FromResult(entry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🚨 [INTEGRITY] Error signing log entry from {Source}", logSource);
                throw new InvalidOperationException($"Failed to sign log entry from source '{logSource}' with integrity verification", ex);
            }
        }

        /// <summary>
        /// Verify a signed log entry
        /// </summary>
        public bool VerifyLogEntry(SignedLogEntry logEntry)
        {
            ArgumentNullException.ThrowIfNull(logEntry);
            
            try
            {
                var tempEntry = new SignedLogEntry
                {
                    Content = logEntry.Content,
                    Source = logEntry.Source,
                    Timestamp = logEntry.Timestamp,
                    ContentHash = logEntry.ContentHash
                };

                var entryJson = JsonSerializer.Serialize(tempEntry, s_jsonOptions);
                
                // Verify content hash
                var expectedHash = CalculateContentHash(logEntry.Content);
                if (logEntry.ContentHash != expectedHash)
                {
                    _logger.LogError("🚨 [INTEGRITY] Log entry content hash mismatch");
                    return false;
                }

                // Verify signature
                return VerifySignature(entryJson, logEntry.Signature, logEntry.PublicKey);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "🚨 [INTEGRITY] JSON parsing error verifying log entry");
                return false;
            }
            catch (CryptographicException ex)
            {
                _logger.LogError(ex, "🚨 [INTEGRITY] Cryptographic error verifying log entry");
                return false;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "🚨 [INTEGRITY] Invalid argument verifying log entry");
                return false;
            }
        }

        /// <summary>
        /// Create integrity hash for model files with metadata
        /// </summary>
        public async Task<ModelIntegrity> CreateModelIntegrityAsync(string modelPath, Dictionary<string, object> metadata)
        {
            try
            {
                var modelHash = await CalculateFileHashAsync(modelPath).ConfigureAwait(false);
                var metadataJson = JsonSerializer.Serialize(metadata);
                var metadataHash = CalculateContentHash(metadataJson);

                var integrity = new ModelIntegrity
                {
                    ModelPath = modelPath,
                    ModelHash = modelHash,
                    MetadataHash = metadataHash,
                    CreatedAt = DateTime.UtcNow,
                    HashAlgorithm = "SHA256"
                };
                
                // Use Replace method for immutable collection pattern
                integrity.ReplaceMetadata(metadata);

                var integrityJson = JsonSerializer.Serialize(integrity, s_jsonOptions);
                integrity.Signature = SignContent(integrityJson);
                integrity.PublicKey = _publicKeyPem;

                _logger.LogInformation("🧠 [INTEGRITY] Created model integrity for {Model}", Path.GetFileName(modelPath));
                return integrity;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🚨 [INTEGRITY] Error creating model integrity: {Model}", modelPath);
                throw new InvalidOperationException($"Failed to create model integrity verification for '{modelPath}'", ex);
            }
        }

        /// <summary>
        /// Sign content using RSA private key
        /// </summary>
        private string SignContent(string content)
        {
            var contentBytes = Encoding.UTF8.GetBytes(content);
            var signatureBytes = _signingKey.SignData(contentBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return Convert.ToBase64String(signatureBytes);
        }

        /// <summary>
        /// Verify signature using RSA public key
        /// </summary>
        private bool VerifySignature(string content, string signature, string publicKeyPem)
        {
            try
            {
                using var rsa = RSA.Create();
                var publicKeyBytes = Convert.FromBase64String(publicKeyPem);
                rsa.ImportRSAPublicKey(publicKeyBytes, out _);

                var contentBytes = Encoding.UTF8.GetBytes(content);
                var signatureBytes = Convert.FromBase64String(signature);

                return rsa.VerifyData(contentBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            catch (CryptographicException ex)
            {
                _logger.LogError(ex, "Cryptographic error verifying signature");
                return false;
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "Format error in signature verification");
                return false;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "Invalid argument in signature verification");
                return false;
            }
        }

        public void Dispose()
        {
            _signingKey?.Dispose();
        }
    }

    /// <summary>
    /// Signed manifest containing file integrity information
    /// </summary>
    public class SignedManifest
    {
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string Version { get; set; } = string.Empty;
        private readonly Dictionary<string, FileIntegrity> _files = new();
        public IReadOnlyDictionary<string, FileIntegrity> Files => _files;
        
        public void ReplaceFiles(IEnumerable<KeyValuePair<string, FileIntegrity>> files)
        {
            _files.Clear();
            if (files != null)
            {
                foreach (var kvp in files)
                    _files[kvp.Key] = kvp.Value;
            }
        }
        
        public void SetFile(string key, FileIntegrity value)
        {
            if (key != null) _files[key] = value;
        }
        public string ContentHash { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public string PublicKey { get; set; } = string.Empty;
    }

    /// <summary>
    /// File integrity information
    /// </summary>
    public class FileIntegrity
    {
        public string Path { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
        public string HashAlgorithm { get; set; } = "SHA256";
    }

    /// <summary>
    /// Signed log entry for audit trail
    /// </summary>
    public class SignedLogEntry
    {
        public string Content { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string ContentHash { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public string PublicKey { get; set; } = string.Empty;
    }

    /// <summary>
    /// Model integrity information
    /// </summary>
    public class ModelIntegrity
    {
        // Private backing field for metadata dictionary
        private readonly Dictionary<string, object> _metadata = new();
        
        public string ModelPath { get; set; } = string.Empty;
        public string ModelHash { get; set; } = string.Empty;
        public string MetadataHash { get; set; } = string.Empty;
        
        // Read-only metadata property  
        public IReadOnlyDictionary<string, object> Metadata => _metadata;
        
        public DateTime CreatedAt { get; set; }
        public string HashAlgorithm { get; set; } = "SHA256";
        public string Signature { get; set; } = string.Empty;
        public string PublicKey { get; set; } = string.Empty;
        
        // Replace method for controlled mutation
        public void ReplaceMetadata(IEnumerable<KeyValuePair<string, object>> items)
        {
            _metadata.Clear();
            if (items != null)
            {
                foreach (var item in items)
                    _metadata[item.Key] = item.Value;
            }
        }
    }

    /// <summary>
    /// Result of manifest verification
    /// </summary>
    public class ManifestVerificationResult
    {
        private readonly List<string> _errors = new();
        
        public bool IsValid { get; set; }
        public DateTime VerifiedAt { get; set; }
        public int VerifiedFiles { get; set; }
        public int MissingFiles { get; set; }
        public int CorruptFiles { get; set; }
        public IReadOnlyList<string> Errors => _errors;
        
        public void ReplaceErrors(IEnumerable<string> errors)
        {
            _errors.Clear();
            if (errors != null) _errors.AddRange(errors);
        }
        
        public void AddError(string error)
        {
            if (!string.IsNullOrEmpty(error)) _errors.Add(error);
        }
    }
}