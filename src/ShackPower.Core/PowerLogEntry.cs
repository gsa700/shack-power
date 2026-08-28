namespace ShackPower.Core;

/// <summary>
/// The read-side mirror of <see cref="PowerLogRecord"/> — deliberately a separate type (the
/// LP-100A pattern): everything off disk is untrusted, so every field is nullable, including
/// rows written by the Python prototype or hand-edited files.
/// </summary>
public sealed record PowerLogEntry
{
    public DateTime? Timestamp { get; init; }
    public double? Volts { get; init; }
    public double? Amps { get; init; }
    public double? Watts { get; init; }
}
