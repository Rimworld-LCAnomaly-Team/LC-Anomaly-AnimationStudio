namespace LCAnimationStudio.Serialization
{
    public enum ValidationSeverity
    {
        Warning,
        Error
    }

    public sealed class ValidationIssue
    {
        public ValidationIssue(ValidationSeverity severity, string filePath, string defName, string trackName, int? tick, string message)
        {
            Severity = severity;
            FilePath = filePath;
            DefName = defName;
            TrackName = trackName;
            Tick = tick;
            Message = message;
        }

        public ValidationSeverity Severity { get; }
        public string FilePath { get; }
        public string DefName { get; }
        public string TrackName { get; }
        public int? Tick { get; }
        public string Message { get; }

        public override string ToString()
        {
            string location = FilePath ?? "<memory>";
            if (!string.IsNullOrEmpty(DefName)) location += " :: " + DefName;
            if (!string.IsNullOrEmpty(TrackName)) location += " :: " + TrackName;
            if (Tick.HasValue) location += " :: tick " + Tick.Value;
            return Severity + ": " + location + " - " + Message;
        }
    }
}
