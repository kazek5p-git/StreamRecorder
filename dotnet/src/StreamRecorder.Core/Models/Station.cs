namespace StreamRecorder.Core.Models;

public sealed class Station
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public Credentials? Credentials { get; set; }

    public static Station Create(string name, string url)
    {
        return new Station
        {
            Id = Guid.NewGuid(),
            Name = name,
            Url = url,
        };
    }
}
