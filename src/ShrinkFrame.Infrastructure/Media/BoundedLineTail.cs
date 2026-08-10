namespace ShrinkFrame.Infrastructure.Media;

internal sealed class BoundedLineTail(int capacity)
{
    private readonly Queue<string> lines = new(capacity);
    public void Add(string line)
    {
        if (lines.Count == capacity) lines.Dequeue();
        lines.Enqueue(line);
    }
    public override string ToString() => string.Join(Environment.NewLine, lines);
}
