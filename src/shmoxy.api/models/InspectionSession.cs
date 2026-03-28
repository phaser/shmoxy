namespace shmoxy.api.models;

public class InspectionSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int RowCount { get; set; }

    public List<InspectionSessionRow> Rows { get; set; } = new();
}
