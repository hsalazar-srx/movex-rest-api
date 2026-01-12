// TransactionConfigurationService.cs - Load, manage, and watch transaction configurations
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Movex.API.Core.Interfaces;
using Movex.API.Core.Models;

namespace Movex.API.Infrastructure.Configuration
{
    public class TransactionConfigurationService : ITransactionConfigurationService, IDisposable
    {
        private readonly ConcurrentDictionary<string, ProgramConfig> _programs = new();
        private readonly string _configPath;
        private readonly FileSystemWatcher _watcher;
        private readonly ConfigurationValidator _validator;
        private bool _disposed = false;

        public TransactionConfigurationService(string configPath = "transactions")
        {
            _configPath = Path.IsPathRooted(configPath) ? configPath : Path.Combine(AppContext.BaseDirectory, configPath);
            
            // Create config directory if it doesn't exist
            if (!Directory.Exists(_configPath))
            {
                Directory.CreateDirectory(_configPath);
                Console.WriteLine($"[ConfigService] Created config directory: {_configPath}");
            }

            _validator = new ConfigurationValidator(_configPath);
            
            // Initial load
            LoadConfigurations();

            // Setup hot-reload watcher
            _watcher = new FileSystemWatcher(_configPath)
            {
                Filter = "*.json",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
            };

            _watcher.Changed += (s, e) =>
            {
                if (e.Name != "schema.json") // Ignore schema file changes
                {
                    Console.WriteLine($"[ConfigService] Configuration file changed: {e.Name}");
                    Task.Delay(100).ContinueWith(_ => LoadConfigurations()); // Small delay to avoid file lock
                }
            };

            _watcher.Created += (s, e) =>
            {
                if (e.Name != "schema.json")
                {
                    Console.WriteLine($"[ConfigService] Configuration file created: {e.Name}");
                    Task.Delay(100).ContinueWith(_ => LoadConfigurations());
                }
            };

            _watcher.Deleted += (s, e) =>
            {
                if (e.Name != "schema.json")
                {
                    Console.WriteLine($"[ConfigService] Configuration file deleted: {e.Name}");
                    var programName = Path.GetFileNameWithoutExtension(e.Name);
                    if (!string.IsNullOrEmpty(programName))
                    {
                        _programs.TryRemove(programName, out _);
                    }
                }
            };

            _watcher.EnableRaisingEvents = true;
            Console.WriteLine($"[ConfigService] Initialized with path: {_configPath}, {_programs.Count} program(s) loaded");
        }

        /// <summary>
        /// Load all program configurations from JSON files
        /// </summary>
        public void LoadConfigurations()
        {
            if (!Directory.Exists(_configPath))
                return;

            var files = Directory.GetFiles(_configPath, "*.json")
                .Where(f => Path.GetFileName(f) != "schema.json")
                .ToList();

            var loadedCount = 0;
            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    
                    // Validate against schema
                    var validation = _validator.Validate(json);
                    if (!validation.IsValid)
                    {
                        Console.Error.WriteLine($"[ConfigService] Invalid config {Path.GetFileName(file)}: {string.Join("; ", validation.Errors)}");
                        continue;
                    }

                    var options = new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    };
                    var config = JsonSerializer.Deserialize<ProgramConfig>(json, options);
                    if (config != null && !string.IsNullOrWhiteSpace(config.Program))
                    {
                        _programs[config.Program] = config;
                        loadedCount++;
                        Console.WriteLine($"[ConfigService] Loaded program '{config.Program}' from {Path.GetFileName(file)}");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[ConfigService] Error loading {Path.GetFileName(file)}: {ex.Message}");
                }
            }

            Console.WriteLine($"[ConfigService] Configuration load complete: {loadedCount} program(s) available");
        }

        /// <summary>
        /// Get a program configuration by name
        /// </summary>
        public ProgramConfig? GetProgram(string programName)
        {
            return _programs.TryGetValue(programName, out var config) ? config : null;
        }

        /// <summary>
        /// Get a transaction definition within a program
        /// </summary>
        public TransactionDefinition? GetTransaction(string programName, string transactionName)
        {
            var program = GetProgram(programName);
            return program?.Transactions
                .FirstOrDefault(t => t.Name.Equals(transactionName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Get all program names
        /// </summary>
        public List<string> GetPrograms()
        {
            return _programs.Keys.ToList();
        }

        /// <summary>
        /// Get all program names (alternate method name for backward compatibility)
        /// </summary>
        public List<string> GetAllPrograms()
        {
            return GetPrograms();
        }

        /// <summary>
        /// Get all transactions for a program
        /// </summary>
        public List<TransactionDefinition> GetTransactions(string programName)
        {
            var program = GetProgram(programName);
            return program?.Transactions ?? new List<TransactionDefinition>();
        }

        /// <summary>
        /// Save a program configuration to file
        /// </summary>
        public async Task SaveProgramAsync(ProgramConfig config)
        {
            if (config == null || string.IsNullOrWhiteSpace(config.Program))
                throw new ArgumentException("Program name is required");

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            
            // Validate before saving
            var validation = _validator.Validate(json);
            if (!validation.IsValid)
                throw new InvalidOperationException($"Invalid configuration: {string.Join("; ", validation.Errors)}");

            var path = Path.Combine(_configPath, $"{config.Program}.json");
            await File.WriteAllTextAsync(path, json);
            
            Console.WriteLine($"[ConfigService] Saved program '{config.Program}' to {Path.GetFileName(path)}");
            
            // Reload to ensure in-memory state is updated
            LoadConfigurations();
        }

        /// <summary>
        /// Update an existing program configuration
        /// </summary>
        public async Task UpdateProgramAsync(string programName, ProgramConfig config)
        {
            var existing = GetProgram(programName);
            if (existing == null)
                throw new KeyNotFoundException($"Program '{programName}' not found");

            if (config.Program != programName)
                throw new ArgumentException("Program name mismatch");

            await SaveProgramAsync(config);
        }

        /// <summary>
        /// Delete a program configuration
        /// </summary>
        public Task DeleteProgramAsync(string programName)
        {
            var path = Path.Combine(_configPath, $"{programName}.json");
            if (File.Exists(path))
            {
                File.Delete(path);
                _programs.TryRemove(programName, out _);
                Console.WriteLine($"[ConfigService] Deleted program '{programName}'");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Manually reload all configurations
        /// </summary>
        public void ReloadAll()
        {
            Console.WriteLine("[ConfigService] Manual reload requested");
            LoadConfigurations();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _watcher?.Dispose();
                _disposed = true;
                Console.WriteLine("[ConfigService] Disposed");
            }
        }
    }
}
