﻿// Copyright (c) VolcanicArts. Licensed under the GPL-3.0 License.
// See the LICENSE file in the repository root for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;

// ReSharper disable SuggestBaseTypeForParameterInConstructor

namespace VRCOSC.Game.Modules.SDK.Attributes;

public abstract class ModuleAttribute
{
    /// <summary>
    /// The metadata for this <see cref="ModuleAttribute"/>
    /// </summary>
    internal ModuleAttributeMetadata Metadata;

    /// <summary>
    /// Initialises this <see cref="ModuleAttribute"/>
    /// </summary>
    internal abstract void Load();

    /// <summary>
    /// Resets this <see cref="ModuleAttribute"/>'s value to its default value
    /// </summary>
    internal abstract void SetDefault();

    /// <summary>
    /// If this <see cref="ModuleAttribute"/>'s value is currently the default value
    /// </summary>
    /// <returns></returns>
    internal abstract bool IsDefault();

    /// <summary>
    /// Attempts to deserialise an object into this <see cref="ModuleAttribute"/>'s value's type
    /// </summary>
    /// <param name="ingestValue">The value to attempt to deserialise</param>
    /// <returns>True if the deserialisation was successful, otherwise false</returns>
    internal abstract bool Deserialise(object ingestValue);

    /// <summary>
    /// Retrieves the value for this <see cref="ModuleAttribute"/> using a provided expected type
    /// </summary>
    /// <typeparam name="TValueType">The type to attempt to convert the value to</typeparam>
    /// <returns>True if the value was converted successfully, otherwise false</returns>
    internal bool GetValue<TValueType>(out TValueType? outValue)
    {
        var value = GetRawValue();

        if (value is TValueType valueAsType)
        {
            outValue = valueAsType;
            return true;
        }

        outValue = default;
        return false;
    }

    /// <summary>
    /// Retrieves the unknown raw typed value for this <see cref="ModuleAttribute"/>.
    /// </summary>
    internal abstract object GetRawValue();

    /// <summary>
    /// Call to request serialisation of this <see cref="ModuleAttribute"/>
    /// </summary>
    internal Action? RequestSerialisation;

    protected ModuleAttribute(ModuleAttributeMetadata metadata)
    {
        Metadata = metadata;
    }
}

public abstract class ModuleSetting : ModuleAttribute
{
    /// <summary>
    /// The metadata for this <see cref="ModuleSetting"/>
    /// </summary>
    internal new ModuleSettingMetadata Metadata => (ModuleSettingMetadata)base.Metadata;

    /// <summary>
    /// The UI component associated with this <see cref="ModuleSetting"/>.
    /// This creates a new instance each time this is called to allow for proper disposal of UI components
    /// </summary>
    internal Container GetDrawableModuleAttribute() => (Container)Activator.CreateInstance(Metadata.DrawableModuleSettingType, this)!;

    /// <summary>
    /// A callback for checking to see if a <see cref="ModuleSetting"/> should be enabled
    /// </summary>
    public Func<bool> IsEnabled = () => true;

    /// <summary>
    /// Addons for this <see cref="ModuleSetting"/>
    /// </summary>
    internal readonly List<ModuleSettingAddon> Addons = new();

    protected ModuleSetting(ModuleSettingMetadata metadata)
        : base(metadata)
    {
    }

    /// <summary>
    /// Add a <see cref="ModuleSettingAddon"/> to this <see cref="ModuleSetting"/>
    /// </summary>
    /// <param name="addon">The <see cref="ModuleSettingAddon"/> to add</param>
    public ModuleSetting AddAddon(ModuleSettingAddon addon)
    {
        Addons.Add(addon);
        return this;
    }
}

public class ModuleParameter : ModuleAttribute
{
    /// <summary>
    /// The metadata for this <see cref="ModuleParameter"/>
    /// </summary>
    internal new ModuleParameterMetadata Metadata => (ModuleParameterMetadata)base.Metadata;

    internal Bindable<string> Name = null!;

    private readonly string defaultName;

    internal override void Load()
    {
        Name = new Bindable<string>(defaultName);
        Name.BindValueChanged(_ => RequestSerialisation?.Invoke());
    }

    internal override bool IsDefault() => Name.IsDefault;
    internal override void SetDefault() => Name.SetDefault();
    internal override object GetRawValue() => Name.Value;

    internal override bool Deserialise(object ingestValue)
    {
        if (ingestValue is not string stringIngestValue) return false;

        Name.Value = stringIngestValue;
        return true;
    }

    public ModuleParameter(ModuleParameterMetadata metadata, string defaultName)
        : base(metadata)
    {
        this.defaultName = defaultName;
    }
}

public abstract class ValueModuleSetting<T> : ModuleSetting
{
    public Bindable<T> Attribute { get; private set; } = null!;

    protected readonly T DefaultValue;

    protected abstract Bindable<T> CreateBindable();

    internal override void Load()
    {
        Attribute = CreateBindable();
        Attribute.BindValueChanged(_ => RequestSerialisation?.Invoke());
    }

    internal override bool IsDefault() => Attribute.IsDefault;
    internal override void SetDefault() => Attribute.SetDefault();

    internal override object GetRawValue() => Attribute.Value!;

    protected ValueModuleSetting(ModuleSettingMetadata metadata, T defaultValue)
        : base(metadata)
    {
        DefaultValue = defaultValue;
    }
}

public class BoolModuleSetting : ValueModuleSetting<bool>
{
    protected override BindableBool CreateBindable() => new(DefaultValue);

    internal override bool Deserialise(object ingestValue)
    {
        if (ingestValue is not bool boolIngestValue) return false;

        Attribute.Value = boolIngestValue;
        return true;
    }

    public BoolModuleSetting(ModuleSettingMetadata metadata, bool defaultValue)
        : base(metadata, defaultValue)
    {
    }
}

public class IntModuleSetting : ValueModuleSetting<int>
{
    protected override BindableNumber<int> CreateBindable() => new(DefaultValue);

    internal override bool Deserialise(object ingestValue)
    {
        if (ingestValue is not long intIngestValue) return false;

        Attribute.Value = (int)intIngestValue;
        return true;
    }

    internal IntModuleSetting(ModuleSettingMetadata metadata, int defaultValue)
        : base(metadata, defaultValue)
    {
    }
}

public class FloatModuleSetting : ValueModuleSetting<float>
{
    protected override BindableNumber<float> CreateBindable() => new(DefaultValue);

    internal override bool Deserialise(object ingestValue)
    {
        if (ingestValue is not double floatIngestValue) return false;

        Attribute.Value = (float)floatIngestValue;
        return true;
    }

    internal FloatModuleSetting(ModuleSettingMetadata metadata, float defaultValue)
        : base(metadata, defaultValue)
    {
    }
}

public class StringModuleSetting : ValueModuleSetting<string>
{
    internal readonly bool EmptyIsValid;

    protected override Bindable<string> CreateBindable() => new(DefaultValue);

    internal override bool Deserialise(object ingestValue)
    {
        if (ingestValue is not string stringIngestValue) return false;

        Attribute.Value = stringIngestValue;
        return true;
    }

    internal StringModuleSetting(ModuleSettingMetadata metadata, bool emptyIsValid, string defaultValue)
        : base(metadata, defaultValue)
    {
        EmptyIsValid = emptyIsValid;
    }
}

public class EnumModuleSetting<TEnum> : ValueModuleSetting<TEnum> where TEnum : Enum
{
    protected override Bindable<TEnum> CreateBindable() => new(DefaultValue);

    internal override bool Deserialise(object ingestValue)
    {
        if (ingestValue is not long intIngestValue) return false;

        Attribute.Value = (TEnum)Enum.ToObject(typeof(TEnum), (int)intIngestValue);
        return true;
    }

    internal EnumModuleSetting(ModuleSettingMetadata metadata, TEnum defaultValue)
        : base(metadata, defaultValue)
    {
    }
}

public class RangedIntModuleSetting : IntModuleSetting
{
    private readonly int minValue;
    private readonly int maxValue;

    protected override BindableNumber<int> CreateBindable()
    {
        var baseBindable = base.CreateBindable();
        baseBindable.MinValue = minValue;
        baseBindable.MaxValue = maxValue;
        return baseBindable;
    }

    internal RangedIntModuleSetting(ModuleSettingMetadata metadata, int defaultValue, int minValue, int maxValue)
        : base(metadata, defaultValue)
    {
        this.minValue = minValue;
        this.maxValue = maxValue;
    }
}

public class RangedFloatModuleSetting : FloatModuleSetting
{
    private readonly float minValue;
    private readonly float maxValue;

    protected override BindableNumber<float> CreateBindable()
    {
        var baseBindable = base.CreateBindable();
        baseBindable.MinValue = minValue;
        baseBindable.MaxValue = maxValue;
        return baseBindable;
    }

    internal RangedFloatModuleSetting(ModuleSettingMetadata metadata, float defaultValue, float minValue, float maxValue)
        : base(metadata, defaultValue)
    {
        this.minValue = minValue;
        this.maxValue = maxValue;
    }
}

public class StringDropdownModuleSetting : IntModuleSetting
{
    internal readonly IEnumerable<string> DropdownValues;

    internal StringDropdownModuleSetting(ModuleSettingMetadata metadata, IEnumerable<string> dropdownValues, int defaultValue)
        : base(metadata, defaultValue)
    {
        DropdownValues = dropdownValues;
    }
}

public abstract class ListModuleSetting<T> : ModuleSetting
{
    public BindableList<T> Attribute = null!;

    private readonly IEnumerable<T> defaultValues;

    internal override object GetRawValue() => Attribute.ToList();

    internal override void Load()
    {
        Attribute = new BindableList<T>(getClonedDefaults());
        Attribute.BindCollectionChanged((_, _) => RequestSerialisation?.Invoke());
    }

    internal override bool IsDefault() => Attribute.SequenceEqual(defaultValues);
    internal override void SetDefault() => Attribute.ReplaceRange(0, Attribute.Count, getClonedDefaults());

    private IEnumerable<T> getClonedDefaults() => defaultValues.Select(CloneValue);
    private IEnumerable<T> jArrayToEnumerable(JArray array) => array.Select(ConstructValue);

    protected abstract T CloneValue(T value);
    protected abstract T ConstructValue(JToken token);

    internal override bool Deserialise(object value)
    {
        Attribute.ReplaceRange(0, Attribute.Count, jArrayToEnumerable((JArray)value));
        return true;
    }

    protected ListModuleSetting(ModuleSettingMetadata metadata, IEnumerable<T> defaultValues)
        : base(metadata)
    {
        this.defaultValues = defaultValues;
    }
}

public abstract class ListValueModuleSetting<T> : ListModuleSetting<Bindable<T>>
{
    internal override object GetRawValue() => Attribute.Select(bindable => bindable.Value).ToList();

    internal override void Load()
    {
        base.Load();

        Attribute.BindCollectionChanged((_, e) =>
        {
            if (e.NewItems is null) return;

            foreach (Bindable<T> newItem in e.NewItems)
            {
                newItem.BindValueChanged(_ => RequestSerialisation?.Invoke());
            }
        }, true);
    }

    protected override Bindable<T> CloneValue(Bindable<T> value) => value.GetUnboundCopy();
    protected override Bindable<T> ConstructValue(JToken token) => new(token.Value<T>()!);

    protected ListValueModuleSetting(ModuleSettingMetadata metadata, IEnumerable<Bindable<T>> defaultValues)
        : base(metadata, defaultValues)
    {
    }
}

public class ListStringModuleSetting : ListValueModuleSetting<string>
{
    public ListStringModuleSetting(ModuleSettingMetadata metadata, IEnumerable<string> defaultValues)
        : base(metadata, defaultValues.Select(value => new Bindable<string>(value)))
    {
    }
}

public class ListMutableKeyValuePairModuleSetting : ListModuleSetting<MutableKeyValuePair>
{
    protected override MutableKeyValuePair CloneValue(MutableKeyValuePair value) => new(value);
    protected override MutableKeyValuePair ConstructValue(JToken token) => token.ToObject<MutableKeyValuePair>()!;

    public ListMutableKeyValuePairModuleSetting(ModuleSettingMetadata metadata, IEnumerable<MutableKeyValuePair> defaultValues)
        : base(metadata, defaultValues)
    {
    }
}
