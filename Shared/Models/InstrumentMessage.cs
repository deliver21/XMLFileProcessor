namespace Shared.Models
{
    public class InstrumentMessage
    {
        public string PackageID { get; set; }
        public List<ModuleInfo> Modules { get; set; } = new();
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    }
}
