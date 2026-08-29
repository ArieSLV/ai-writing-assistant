namespace AiWritingAssistant.Audio;

internal sealed class AudioLevelBuffer
{
    private readonly object _gate = new();
    private readonly float[] _values;
    private int _next;
    private int _count;

    public AudioLevelBuffer(int capacity = 48)
    {
        if (capacity is < 8 or > 256)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _values = new float[capacity];
    }

    public int Capacity => _values.Length;

    public void Add(float value)
    {
        value = Math.Clamp(float.IsFinite(value) ? value : 0.0f, 0.0f, 1.0f);

        lock (_gate)
        {
            _values[_next] = value;
            _next = (_next + 1) % _values.Length;
            _count = Math.Min(_count + 1, _values.Length);
        }
    }

    public float[] Snapshot()
    {
        lock (_gate)
        {
            var result = new float[_count];
            var first = (_next - _count + _values.Length) % _values.Length;

            for (var index = 0; index < _count; index++)
                result[index] = _values[(first + index) % _values.Length];

            return result;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_values);
            _next = 0;
            _count = 0;
        }
    }
}
