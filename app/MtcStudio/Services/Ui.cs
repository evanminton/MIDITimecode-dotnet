using Microsoft.Maui.Controls.Shapes;

namespace MtcStudio;

/// <summary>Colours and small view factories shared by the code-built pages.</summary>
public static class Ui
{
    public static readonly Color Background = Color.FromArgb("#141414");
    public static readonly Color Surface = Color.FromArgb("#1E1E1E");
    public static readonly Color Border = Color.FromArgb("#333333");
    public static readonly Color Foreground = Color.FromArgb("#EDEDED");
    public static readonly Color Muted = Color.FromArgb("#9A9A9A");
    public static readonly Color Accent = Color.FromArgb("#F5A623");
    public static readonly Color Good = Color.FromArgb("#3DD68C");
    public static readonly Color Warn = Color.FromArgb("#F5A623");
    public static readonly Color Bad = Color.FromArgb("#E5484D");

    public static string Mono => "Consolas";

    public static readonly Color Info = Color.FromArgb("#5EA3F7");

    /// <summary>A large monospaced time code readout.</summary>
    public static Label Display(double size = 64) => new()
    {
        Text = "--:--:--:--",
        FontFamily = Mono,
        FontSize = size,
        FontAttributes = FontAttributes.Bold,
        TextColor = Muted,
        HorizontalTextAlignment = TextAlignment.Center,
        LineBreakMode = LineBreakMode.NoWrap,
    };

    public static ContentPage Page(string title, View content) => new()
    {
        Title = title,
        BackgroundColor = Background,
        Content = new ScrollView { Content = content },
    };

    public static VerticalStackLayout Column(params View[] children)
    {
        var layout = new VerticalStackLayout { Spacing = 10, Padding = new Thickness(16) };
        foreach (var c in children) layout.Children.Add(c);
        return layout;
    }

    public static HorizontalStackLayout Row(params View[] children)
    {
        var layout = new HorizontalStackLayout { Spacing = 8 };
        foreach (var c in children) layout.Children.Add(c);
        return layout;
    }

    public static FlexLayout Wrap(params View[] children)
    {
        var layout = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
            JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Start,
        };
        foreach (var c in children)
        {
            c.Margin = new Thickness(0, 0, 8, 8);
            layout.Children.Add(c);
        }
        return layout;
    }

    public static Border Section(string title, params View[] children)
    {
        var stack = new VerticalStackLayout { Spacing = 8 };
        stack.Children.Add(new Label { Text = title, TextColor = Accent, FontAttributes = FontAttributes.Bold, FontSize = 15 });
        foreach (var c in children) stack.Children.Add(c);
        return new Border
        {
            Stroke = Border,
            StrokeThickness = 1,
            BackgroundColor = Surface,
            Padding = new Thickness(12),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) },
            Content = stack,
        };
    }

    public static Label Text(string text = "", double size = 14, bool bold = false, Color? color = null) => new()
    {
        Text = text,
        FontSize = size,
        FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
        TextColor = color ?? Foreground,
        LineBreakMode = LineBreakMode.WordWrap,
    };

    public static Label MonoText(string text = "", double size = 13, Color? color = null) => new()
    {
        Text = text,
        FontFamily = Mono,
        FontSize = size,
        TextColor = color ?? Foreground,
        LineBreakMode = LineBreakMode.WordWrap,
    };

    public static Label Caption(string text) => Text(text, 12, color: Muted);

    public static Button Button(string text, Action onClick, Color? color = null)
    {
        var b = new Button
        {
            Text = text,
            BackgroundColor = color ?? Border,
            TextColor = Foreground,
            CornerRadius = 6,
            Padding = new Thickness(14, 6),
        };
        b.Clicked += (_, _) => onClick();
        return b;
    }

    public static Entry Entry(string text, string placeholder, double width = 160) => new()
    {
        Text = text,
        Placeholder = placeholder,
        FontFamily = Mono,
        TextColor = Foreground,
        PlaceholderColor = Muted,
        BackgroundColor = Background,
        WidthRequest = width,
    };

    public static Picker Picker(string title, IList<string> items, int selected, double width = 220) => new()
    {
        Title = title,
        ItemsSource = items.ToList(),
        SelectedIndex = selected,
        TextColor = Foreground,
        TitleColor = Muted,
        BackgroundColor = Background,
        WidthRequest = width,
    };

    public static View Labeled(string label, View view) =>
        new VerticalStackLayout { Spacing = 2, Children = { Caption(label), view } };
}
