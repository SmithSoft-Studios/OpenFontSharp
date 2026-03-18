namespace OpenFontSharp.Shaping;

/// <summary>
/// Detects whether text contains scripts that require complex shaping
/// (Arabic, Devanagari, CJK, Thai, etc.) or can be handled by basic
/// Latin/Cyrillic/Greek shaping.
/// </summary>
public static class ScriptDetector
{
    /// <summary>
    /// Returns true if the text contains any characters from scripts
    /// that require complex shaping (contextual forms, reordering, joining).
    /// Returns false for Latin, Cyrillic, Greek, Common, and Inherited scripts.
    /// </summary>
    public static bool RequiresComplexShaping(ReadOnlySpan<char> text)
    {
        foreach (char c in text)
        {
            if (IsComplexScriptChar(c))
                return true;
        }
        return false;
    }

    /// <summary>
    /// String overload for convenience.
    /// </summary>
    public static bool RequiresComplexShaping(string text)
        => RequiresComplexShaping(text.AsSpan());

    /// <summary>
    /// Checks if a character belongs to a script that requires complex shaping.
    /// Based on Unicode script property ranges (UAX #24).
    /// </summary>
    private static bool IsComplexScriptChar(char c)
    {
        // Simple scripts (no complex shaping needed):
        // - Basic Latin (0000-007F)
        // - Latin Extended (0080-024F, 1E00-1EFF, 2C60-2C7F, A720-A7FF)
        // - Cyrillic (0400-052F, 2DE0-2DFF, A640-A69F)
        // - Greek (0370-03FF, 1F00-1FFF)
        // - Common: numbers, punctuation, symbols (2000-206F, 2070-209F, 20A0-20CF, etc.)
        // - General punctuation, math operators, arrows, box drawing, etc.

        // Complex scripts that need HarfBuzz:
        return c switch
        {
            // Arabic (0600-06FF, 0750-077F, 08A0-08FF, FB50-FDFF, FE70-FEFF)
            >= '\u0600' and <= '\u06FF' => true,
            >= '\u0750' and <= '\u077F' => true,
            >= '\u08A0' and <= '\u08FF' => true,
            >= '\uFB50' and <= '\uFDFF' => true,
            >= '\uFE70' and <= '\uFEFF' => true,

            // Hebrew (0590-05FF, FB1D-FB4F)
            >= '\u0590' and <= '\u05FF' => true,
            >= '\uFB1D' and <= '\uFB4F' => true,

            // Devanagari (0900-097F)
            >= '\u0900' and <= '\u097F' => true,

            // Bengali (0980-09FF)
            >= '\u0980' and <= '\u09FF' => true,

            // Tamil (0B80-0BFF)
            >= '\u0B80' and <= '\u0BFF' => true,

            // Thai (0E00-0E7F)
            >= '\u0E00' and <= '\u0E7F' => true,

            // Lao (0E80-0EFF)
            >= '\u0E80' and <= '\u0EFF' => true,

            // Tibetan (0F00-0FFF)
            >= '\u0F00' and <= '\u0FFF' => true,

            // Myanmar (1000-109F)
            >= '\u1000' and <= '\u109F' => true,

            // Khmer (1780-17FF)
            >= '\u1780' and <= '\u17FF' => true,

            // CJK Unified Ideographs (4E00-9FFF)
            >= '\u4E00' and <= '\u9FFF' => true,

            // CJK Extension A (3400-4DBF)
            >= '\u3400' and <= '\u4DBF' => true,

            // Hiragana (3040-309F)
            >= '\u3040' and <= '\u309F' => true,

            // Katakana (30A0-30FF)
            >= '\u30A0' and <= '\u30FF' => true,

            // Hangul Syllables (AC00-D7AF)
            >= '\uAC00' and <= '\uD7AF' => true,

            // Hangul Jamo (1100-11FF)
            >= '\u1100' and <= '\u11FF' => true,

            // Everything else: simple (Latin extended, Cyrillic, Greek, symbols, etc.)
            _ => false
        };
    }
}
