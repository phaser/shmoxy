namespace shmoxy.api.models.dto;

public class RetentionPolicyDto
{
    public bool Enabled { get; set; }
    public int? MaxAgeDays { get; set; }
    public int? MaxCount { get; set; }
}
