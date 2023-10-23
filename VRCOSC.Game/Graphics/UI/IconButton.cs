﻿// Copyright (c) VolcanicArts. Licensed under the GPL-3.0 License.
// See the LICENSE file in the repository root for full license text.

using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace VRCOSC.Game.Graphics.UI;

public partial class IconButton : ClickableContainer
{
    public Color4 BackgroundColour { get; init; } = Color4.Black;
    public IconUsage Icon { get; init; } = FontAwesome.Regular.Angry;
    public float IconSize { get; init; } = 20;
    public Color4 IconColour { get; init; } = Color4.White;

    private Box background = null!;
    private SpriteIcon icon = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Child = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Masking = true,
            BorderThickness = 3,
            BorderColour = BackgroundColour,
            CornerRadius = 5,
            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = BackgroundColour,
                    Alpha = 0,
                    AlwaysPresent = true
                },
                icon = new SpriteIcon
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Icon = Icon,
                    Size = new Vector2(IconSize),
                    Colour = IconColour,
                    Shadow = true,
                    ShadowColour = Colours.Black.Opacity(0.75f),
                    ShadowOffset = Vector2.Zero
                }
            }
        };
    }

    protected override bool OnHover(HoverEvent e)
    {
        fadeInBackground();
        return true;
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        if (e.IsPressed(MouseButton.Left)) return;

        fadeOutBackground();
    }

    protected override bool OnMouseDown(MouseDownEvent e)
    {
        if (e.Button != MouseButton.Left) return false;

        Child.ScaleTo(0.95f, 500, Easing.OutQuart);
        return true;
    }

    protected override void OnMouseUp(MouseUpEvent e)
    {
        if (e.Button != MouseButton.Left) return;

        Child.ScaleTo(1f, 500, Easing.OutQuart);

        if (!IsHovered)
        {
            fadeOutBackground();
        }
    }

    private void fadeInBackground()
    {
        background.FadeInFromZero(100, Easing.OutQuart);
        icon.TransformTo(nameof(SpriteText.ShadowOffset), new Vector2(0, 0.05f));
    }

    private void fadeOutBackground()
    {
        background.FadeOutFromOne(100, Easing.OutQuart);
        icon.TransformTo(nameof(SpriteText.ShadowOffset), Vector2.Zero);
    }
}
