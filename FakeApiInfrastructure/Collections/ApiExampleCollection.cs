namespace FakeApiInfrastructure.Collections;

public class ApiExampleCollection
{
    public int UserId { get; set; }
    public int Id { get; set; }          // Identity principal
    public string Title { get; set; } = string.Empty;
    public bool Completed { get; set; }
}
