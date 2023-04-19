﻿// Copyright (c) VolcanicArts. Licensed under the GPL-3.0 License.
// See the LICENSE file in the repository root for full license text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using VRCOSC.Game.ChatBox;
using VRCOSC.Game.Graphics.Themes;
using VRCOSC.Game.Graphics.UI.Text;

namespace VRCOSC.Game.Graphics.ChatBox.Metadata;

public partial class MetadataString : Container
{
    [Resolved]
    private ChatBoxManager chatBoxManager { get; set; } = null!;

    public required string Label { get; init; }
    public required Bindable<string> Current { get; init; }

    private LocalTextBox inputTextBox = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;

        Children = new Drawable[]
        {
            new Container
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Width = 0.5f,
                Child = new SpriteText
                {
                    Font = FrameworkFont.Regular.With(size: 25),
                    Text = Label
                }
            },
            new Container
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                RelativeSizeAxes = Axes.Both,
                Width = 0.5f,
                Child = inputTextBox = new LocalTextBox
                {
                    RelativeSizeAxes = Axes.Both,
                    CornerRadius = 5,
                    MinimumLength = 2,
                }
            }
        };
    }

    protected override void LoadComplete()
    {
        inputTextBox.Text = Current.Value;
        inputTextBox.OnValidEntry += value => Current.Value = value;
    }

    private partial class LocalTextBox : StringTextBox
    {
        [BackgroundDependencyLoader]
        private void load()
        {
            BackgroundUnfocused = ThemeManager.Current[ThemeAttribute.Darker];
            BackgroundFocused = ThemeManager.Current[ThemeAttribute.Darker];
        }
    }
}
