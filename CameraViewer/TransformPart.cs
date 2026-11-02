using System.Text.Json.Serialization;

namespace CameraViewer
{
    public enum TransformPartType
    {
        Name,
        DateTime,
    }

    public class TransformPart
    {
        public TransformPartType PartType { get; set; } = TransformPartType.Name;
        public int SourceIndex { get; set; }
        public string Format { get; set; } = "";
    }
}
