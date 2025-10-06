using Shared.Models;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace FileParserService.Services
{
    public class FilePollingService : BackgroundService
    {
        private readonly ILogger<FilePollingService> _log;
        private readonly IConfiguration _cfg;
        private readonly RabbitPublisher _publisher;
        private readonly int _interval;
        private readonly string _folder;
        private readonly string _processed;
        private readonly string _failed;

        public FilePollingService(ILogger<FilePollingService> log, IConfiguration cfg, RabbitPublisher publisher)
        {
            _log = log;
            _cfg = cfg;
            _publisher = publisher;

            _interval = cfg.GetValue<int>("Watch:PollIntervalMs", 1000);
            _folder = cfg.GetValue<string>("Watch:Folder", "Incoming");
            _processed = cfg.GetValue<string>("Watch:ProcessedFolder", "Processed");
            _failed = cfg.GetValue<string>("Watch:FailedFolder", "Failed");

            Directory.CreateDirectory(_folder);
            Directory.CreateDirectory(_processed);
            Directory.CreateDirectory(_failed);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _log.LogInformation("FilePollingService started. Watching folder: {folder}", _folder);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var files = Directory.GetFiles(_folder);
                    foreach (var file in files)
                    {
                        // start processing per file on its own Task
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                _log.LogInformation("Processing file {file}", file);
                                var content = await ReadFileSafeAsync(file);
                                var message = ParseStatusXml(content);
                                if (message != null)
                                {
                                    var json = JsonSerializer.Serialize(message);
                                    _publisher.Publish(json);
                                    var dest = Path.Combine(_processed, Path.GetFileName(file));
                                    File.Move(file, dest, overwrite: true);
                                    _log.LogInformation("File {file} processed and moved to {dest}", file, dest);
                                }
                                else
                                {
                                    var dest = Path.Combine(_failed, Path.GetFileName(file));
                                    File.Move(file, dest, overwrite: true);
                                    _log.LogWarning("File {file} could not be parsed. Moved to {dest}", file, dest);
                                }
                            }
                            catch (Exception ex)
                            {
                                _log.LogError(ex, "Error processing file {file}", file);
                                try
                                {
                                    var dest = Path.Combine(_failed, Path.GetFileName(file));
                                    File.Move(file, dest, overwrite: true);
                                }
                                catch { }
                            }
                        }, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error while polling directory");
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }

        private async Task<string> ReadFileSafeAsync(string path)
        {
            // retry for file lock
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var sr = new StreamReader(fs, Encoding.UTF8);
                    return await sr.ReadToEndAsync();
                }
                catch (IOException)
                {
                    await Task.Delay(100);
                }
            }
            throw new IOException($"Cannot read file {path}");
        }

        private InstrumentMessage ParseStatusXml(string xml)
        {
            try
            {
                var doc = XDocument.Parse(xml);
                var ns = doc.Root?.Name.Namespace;
                var packageId = doc.Root?.Element(ns + "PackageID")?.Value ?? "unknown";
                var devStatuses = doc.Root?.Elements(ns + "DeviceStatus") ?? Enumerable.Empty<XElement>();

                var rnd = new Random();
                var states = new[] { "Online", "Run", "NotReady", "Offline" };

                var msg = new InstrumentMessage
                {
                    PackageID = packageId,
                    Modules = devStatuses.Select(ds =>
                    {
                        var cat = ds.Element(ns + "ModuleCategoryID")?.Value ?? "UNKNOWN";
                        var rapidControl = ds.Element(ns + "RapidControlStatus")?.Value ?? string.Empty;
                        // inner XML string; parse to find <ModuleState> if present
                        string stateFromInner = null;
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(rapidControl))
                            {
                                // include XML declaration; strip if needed
                                var trimmed = rapidControl.Trim();
                                // parse inner xml
                                var inner = XElement.Parse(trimmed);
                                stateFromInner = inner.Elements().FirstOrDefault(e => e.Name.LocalName == "ModuleState")?.Value;
                            }
                        }
                        catch
                        {
                            
                        }
                        // random state ignoring original (task required random change)
                        var newState = states[rnd.Next(states.Length)];
                        return new ModuleInfo
                        {
                            ModuleCategoryID = cat,
                            ModuleState = newState
                        };
                    }).ToList()
                };
                return msg;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
