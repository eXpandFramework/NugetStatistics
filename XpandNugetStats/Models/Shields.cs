namespace XpandNugetStats.Models{
    public class Shields{
        public int SchemaVersion{ get; } = 1;
        public string Label{ get; } = "Total";
        public string Message{ get; set; }
        public string Color{ get; } = "Green";
    }
}