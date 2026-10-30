using System.Text.RegularExpressions;

namespace ShotLite;

/// <summary>A selection shape such as 9:16, kept as the user wrote it: 21:9 is more familiar than 7:3.</summary>
internal readonly partial record struct AspectRatio(int Width, int Height)
{
    public const int MaxCount = 8;
    private const int MaxValue = 100;
    /// <summary>Limits how long and thin a shape can get; even 32:9 is well inside it.</summary>
    private const int MaxSideRatio = 10;

    public static readonly string[] Defaults = ["9:16", "4:3", "16:9"];

    /// <summary>Selections grow by this size: it keeps the ratio exact and both sides even, as H.264 needs.</summary>
    public int StepWidth => Width * 2 / GreatestCommonDivisor(Width, Height);
    public int StepHeight => Height * 2 / GreatestCommonDivisor(Width, Height);

    public override string ToString() => $"{Width}:{Height}";

    public static bool TryParse(string? text, out AspectRatio ratio)
    {
        ratio = default;
        var match = text == null ? Match.Empty : Format().Match(text.Trim());
        if (!match.Success)
            return false;

        int width = int.Parse(match.Groups[1].Value), height = int.Parse(match.Groups[2].Value);
        if (width is < 1 or > MaxValue || height is < 1 or > MaxValue
            || width > height * MaxSideRatio || height > width * MaxSideRatio)
            return false;

        ratio = new AspectRatio(width, height);
        return true;
    }

    /// <summary>Parses a comma-separated list; every item must be valid, different and within the count limit.</summary>
    /// <param name="invalid">The first rejected item.</param>
    public static bool TryParseList(string text, out List<AspectRatio> ratios, out string invalid)
    {
        ratios = new List<AspectRatio>();
        invalid = "";
        foreach (var item in text.Split(','))
        {
            if (!TryParse(item, out var ratio) || ratios.Exists(ratio.IsSameShape) || ratios.Count == MaxCount)
            {
                invalid = item.Trim();
                return false;
            }
            ratios.Add(ratio);
        }
        return true;
    }

    /// <summary>True for equal proportions written differently, such as 9:16 and 18:32.</summary>
    public bool IsSameShape(AspectRatio other) => Width * other.Height == Height * other.Width;

    private static int GreatestCommonDivisor(int a, int b) => b == 0 ? a : GreatestCommonDivisor(b, a % b);

    [GeneratedRegex("^([0-9]{1,3}):([0-9]{1,3})$")]
    private static partial Regex Format();
}
