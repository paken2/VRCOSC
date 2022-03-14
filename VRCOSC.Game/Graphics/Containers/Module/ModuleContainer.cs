﻿using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using VRCOSC.Game.Modules;

namespace VRCOSC.Game.Graphics.Containers.Module;

public class ModuleContainer : Container
{
    public Modules.Module SourceModule { get; init; }

    private FillFlowContainer<ModuleSettingContainer> settingsFlow { get; set; }

    [BackgroundDependencyLoader]
    private void load()
    {
        if (SourceModule == null)
            throw new ArgumentNullException(nameof(SourceModule));

        Children = new Drawable[]
        {
            new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Colour = VRCOSCColour.Gray3,
            },
            new Container
            {
                Name = "Content",
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding
                {
                    Vertical = 30,
                    Horizontal = 10
                },
                Child = new FillFlowContainer
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Spacing = new Vector2(0, 10),
                    Direction = FillDirection.Vertical,
                    Children = new Drawable[]
                    {
                        new TextFlowContainer(t => t.Font = FrameworkFont.Regular.With(size: 50))
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            TextAnchor = Anchor.TopCentre,
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Text = SourceModule.Title
                        },
                        new TextFlowContainer(t =>
                        {
                            t.Font = FrameworkFont.Regular.With(size: 30);
                            t.Colour = VRCOSCColour.Gray9;
                        })
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            TextAnchor = Anchor.TopCentre,
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Text = SourceModule.Description
                        },
                        new Container
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            RelativeSizeAxes = Axes.X,
                            Size = new Vector2(0.95f, 5),
                            Masking = true,
                            CornerRadius = 4,
                            Child = new Box
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                RelativeSizeAxes = Axes.Both,
                                Colour = VRCOSCColour.Gray1,
                            }
                        },
                        settingsFlow = new FillFlowContainer<ModuleSettingContainer>
                        {
                            Name = "Settings",
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 5)
                        }
                    }
                }
            }
        };

        SourceModule.Settings.ForEach(setting =>
        {
            if (setting.GetType() == typeof(ModuleSettingString))
            {
                settingsFlow.Add(new ModuleSettingStringContainer
                {
                    SourceSetting = (ModuleSettingString)setting
                });
            }
            else if (setting.GetType() == typeof(ModuleSettingInt))
            {
                settingsFlow.Add(new ModuleSettingIntContainer
                {
                    SourceSetting = (ModuleSettingInt)setting
                });
            }
            else if (setting.GetType() == typeof(ModuleSettingBool))
            {
                settingsFlow.Add(new ModuleSettingBoolContainer
                {
                    SourceSetting = (ModuleSettingBool)setting
                });
            }
        });
    }
}
