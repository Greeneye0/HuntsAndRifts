using System;
using System.Collections.Generic;
using TMPro;

namespace HuntsAndRifts;

internal static class UiTextState
{
    private sealed class State
    {
        internal TMP_Text Text;
        internal string Value;
        internal bool Rich;
        internal FontStyles Style;
        internal RectSnapshot Rect;
    }
    private static readonly Dictionary<IntPtr, State> Saved = new();
    internal static void Track(TMP_Text text)
    {
        if (text == null || Saved.ContainsKey(text.Pointer)) return;
        Saved[text.Pointer] = new State { Text = text, Value = text.text,
            Rich = text.richText, Style = text.fontStyle, Rect = new RectSnapshot(text.rectTransform) };
    }
    internal static void Restore()
    {
        foreach (var state in Saved.Values)
        {
            if (state.Text == null) continue;
            state.Text.text = state.Value; state.Text.richText = state.Rich; state.Text.fontStyle = state.Style;
            state.Rect.Restore();
        }
        Saved.Clear();
    }
}
