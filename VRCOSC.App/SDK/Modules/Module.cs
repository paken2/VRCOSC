// Copyright (c) VolcanicArts. Licensed under the GPL-3.0 License.
// See the LICENSE file in the repository root for full license text.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using VRCOSC.App.ChatBox;
using VRCOSC.App.ChatBox.Clips;
using VRCOSC.App.ChatBox.Clips.Variables;
using VRCOSC.App.ChatBox.Clips.Variables.Instances;
using VRCOSC.App.Modules;
using VRCOSC.App.OSC.VRChat;
using VRCOSC.App.Packages;
using VRCOSC.App.Pages.Modules.Settings;
using VRCOSC.App.SDK.Modules.Attributes;
using VRCOSC.App.SDK.Modules.Attributes.Parameters;
using VRCOSC.App.SDK.Modules.Attributes.Settings;
using VRCOSC.App.SDK.Modules.Attributes.Types;
using VRCOSC.App.SDK.OVR;
using VRCOSC.App.SDK.Parameters;
using VRCOSC.App.SDK.VRChat;
using VRCOSC.App.Serialisation;
using VRCOSC.App.Settings;
using VRCOSC.App.Utils;

namespace VRCOSC.App.SDK.Modules;

public abstract class Module
{
    internal string PackageID { get; set; } = null!;
    internal string ID => GetType().Name.ToLowerInvariant();
    internal string FullID => $"{PackageID}.{ID}";

    public Observable<bool> Enabled { get; } = new();
    internal Observable<ModuleState> State { get; } = new(ModuleState.Stopped);

    protected OVRClient OVRClient => AppManager.GetInstance().OVRClient;
    protected VRChatClient VRChatClient => AppManager.GetInstance().VRChatClient;

    public string Title => GetType().GetCustomAttribute<ModuleTitleAttribute>()?.Title ?? "PLACEHOLDER";
    public string ShortDescription => GetType().GetCustomAttribute<ModuleDescriptionAttribute>()?.ShortDescription ?? string.Empty;
    public ModuleType Type => GetType().GetCustomAttribute<ModuleTypeAttribute>()?.Type ?? ModuleType.Generic;
    public Brush Colour => Type.ToColour();

    public string TitleWithPackage => PackageManager.GetInstance().GetPackage(PackageID) is null ? $"(Local) {Title}" : Title;

    // Cached pre-computed lookups
    private readonly Dictionary<string, Enum> parameterNameEnum = new();
    private readonly Dictionary<string, Regex> parameterNameRegex = new();

    internal readonly Dictionary<Enum, ModuleParameter> Parameters = new();
    internal readonly Dictionary<string, ModuleSetting> Settings = new();

    internal readonly Dictionary<string, List<string>> Groups = new();
    internal readonly Dictionary<ModulePersistentAttribute, PropertyInfo> PersistentProperties = new();

    private readonly List<Repeater> updateTasks = new();
    internal readonly List<MethodInfo> ChatBoxUpdateMethods = new();

    private SerialisationManager moduleSerialisationManager = null!;
    private SerialisationManager persistenceSerialisationManager = null!;

    internal Page? RuntimePage;

    private readonly object loadLock = new();

    public bool HasSettings => Settings.Count != 0;
    public bool HasParameters => Parameters.Count != 0;

    protected virtual bool ShouldUsePersistence => true;

    protected Module()
    {
        State.Subscribe(newState => Log(newState.ToString()));
    }

    private static Regex parameterToRegex(string parameterName)
    {
        var pattern = "^"; // start of string
        pattern += parameterName.Replace("/", @"\/").Replace("*", @"(\S*)");
        pattern += "$"; // end of string

        return new Regex(pattern);
    }

    internal void InjectDependencies(SerialisationManager moduleSerialisationManager, SerialisationManager persistenceSerialisationManager)
    {
        this.moduleSerialisationManager = moduleSerialisationManager;
        this.persistenceSerialisationManager = persistenceSerialisationManager;
    }

    internal void Load(string filePathOverride = "")
    {
        lock (loadLock)
        {
            Settings.Clear();
            Parameters.Clear();
            Groups.Clear();

            OnPreLoad();

            Settings.Values.ForEach(moduleSetting => moduleSetting.PreDeserialise());
            Parameters.Values.ForEach(moduleParameter => moduleParameter.PreDeserialise());

            moduleSerialisationManager.Deserialise(string.IsNullOrEmpty(filePathOverride), filePathOverride);

            Settings.Values.ForEach(moduleSetting => moduleSetting.PostDeserialise());
            Parameters.Values.ForEach(moduleParameter => moduleParameter.PostDeserialise());

            cachePersistentProperties();

            Enabled.Subscribe(_ => moduleSerialisationManager.Serialise());
            Settings.Values.ForEach(moduleSetting => moduleSetting.RequestSerialisation = () => moduleSerialisationManager.Serialise());
            Parameters.Values.ForEach(moduleParameter => moduleParameter.RequestSerialisation = () => moduleSerialisationManager.Serialise());

            OnPostLoad();
        }
    }

    internal void ImportConfig(string filePathOverride)
    {
        ChatBoxManager.GetInstance().Unload(FullID);

        Load(filePathOverride);

        ChatBoxManager.GetInstance().Deserialise();
    }

    #region Persistence

    internal bool TryGetPersistentProperty(string key, [NotNullWhen(true)] out PropertyInfo? property)
    {
        property = PersistentProperties.SingleOrDefault(property => property.Key.SerialisedName == key).Value;
        return property is not null;
    }

    private void cachePersistentProperties()
    {
        try
        {
            PersistentProperties.Clear();

            GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy).ForEach(info =>
            {
                var isDefined = info.IsDefined(typeof(ModulePersistentAttribute));
                if (!isDefined) return;

                if (!info.CanRead || !info.CanWrite) throw new InvalidOperationException($"Property '{info.Name}' must be declared with get/set to have persistence");

                PersistentProperties.Add(info.GetCustomAttribute<ModulePersistentAttribute>()!, info);
            });
        }
        catch (Exception e)
        {
            ExceptionHandler.Handle(e, $"{FullID} encountered an error while trying to cache the persistent properties");
        }
    }

    private void loadPersistentProperties()
    {
        if (!PersistentProperties.Any() || !ShouldUsePersistence) return;

        persistenceSerialisationManager.Deserialise();
    }

    private void savePersistentProperties()
    {
        if (!PersistentProperties.Any() || !ShouldUsePersistence) return;

        persistenceSerialisationManager.Serialise();
    }

    #endregion

    internal async Task Start()
    {
        State.Value = ModuleState.Starting;

        parameterNameEnum.Clear();
        parameterNameRegex.Clear();

        var validParameters = Parameters.Where(parameter => !string.IsNullOrWhiteSpace(parameter.Value.Name.Value)).ToList();

        validParameters.ForEach(pair =>
        {
            parameterNameEnum.Add(pair.Value.Name.Value!, pair.Key);
            parameterNameRegex.Add(pair.Value.Name.Value!, parameterToRegex(pair.Value.Name.Value!));
        });

        loadPersistentProperties();

        var startResult = await OnModuleStart();

        if (!startResult)
        {
            await Stop();
            return;
        }

        State.Value = ModuleState.Started;

        initialiseUpdateAttributes(GetType());
    }

    internal async Task Stop()
    {
        State.Value = ModuleState.Stopping;

        foreach (var updateTask in updateTasks) await updateTask.StopAsync();
        updateTasks.Clear();
        await OnModuleStop();

        savePersistentProperties();

        State.Value = ModuleState.Stopped;
    }

    private void updateMethod(MethodBase method)
    {
        try
        {
            method.Invoke(this, null);
        }
        catch (Exception e)
        {
            ExceptionHandler.Handle(e, $"{FullID} experienced an exception calling method {method.Name}");
        }
    }

    private void initialiseUpdateAttributes(Type? type)
    {
        if (type is null) return;

        initialiseUpdateAttributes(type.BaseType);

        type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .ForEach(method =>
            {
                var updateAttribute = method.GetCustomAttribute<ModuleUpdateAttribute>();
                if (updateAttribute is null) return;

                switch (updateAttribute.Mode)
                {
                    case ModuleUpdateMode.Custom:
                        var updateTask = new Repeater(() => updateMethod(method));
                        updateTask.Start(TimeSpan.FromMilliseconds(updateAttribute.DeltaMilliseconds));
                        updateTasks.Add(updateTask);
                        if (updateAttribute.UpdateImmediately) updateMethod(method);
                        break;

                    case ModuleUpdateMode.ChatBox:
                        ChatBoxUpdateMethods.Add(method);
                        break;
                }
            });
    }

    #region Update

    internal void ChatBoxUpdate()
    {
        ChatBoxUpdateMethods.ForEach(invokeMethod);
    }

    private void invokeMethod(MethodBase method)
    {
        try
        {
            method.Invoke(this, null);
        }
        catch (Exception e)
        {
            ExceptionHandler.Handle(e, $"{FullID} experienced an exception calling method {method.Name}");
        }
    }

    #endregion

    #region SDK

    protected virtual void OnPreLoad()
    {
    }

    protected virtual void OnPostLoad()
    {
    }

    protected virtual Task<bool> OnModuleStart() => Task.FromResult(true);

    protected virtual Task OnModuleStop() => Task.CompletedTask;

    /// <summary>
    /// Logs to the terminal when the module is running
    /// </summary>
    /// <param name="message">The message to log to the terminal</param>
    protected void Log(string message)
    {
        Logger.Log($"[{Title}]: {message}", "terminal");
    }

    /// <summary>
    /// Logs to a module debug file when enabled in the settings
    /// </summary>
    /// <param name="message">The message to log to the file</param>
    protected void LogDebug(string message)
    {
        if (!SettingsManager.GetInstance().GetValue<bool>(VRCOSCSetting.EnableAppDebug)) return;

        Logger.Log($"[{Title}]: {message}", "module-debug");
    }

    /// <summary>
    /// Registers a parameter with a lookup to allow the user to customise the parameter name
    /// </summary>
    /// <typeparam name="T">The value type of this <see cref="ModuleParameter"/></typeparam>
    /// <param name="lookup">The lookup of this parameter, used as a reference when calling <see cref="SendParameter(Enum,object)"/></param>
    /// <param name="defaultName">The default name of the parameter</param>
    /// <param name="title">The title of the parameter</param>
    /// <param name="description">A short description of the parameter</param>
    /// <param name="mode">Whether the parameter can read from or write to VRChat</param>
    /// <param name="legacy">Whether the parameter is legacy and should no longer be used in favour of the other parameters</param>
    protected void RegisterParameter<T>(Enum lookup, string defaultName, ParameterMode mode, string title, string description, bool legacy = false) where T : struct
    {
        Parameters.Add(lookup, new ModuleParameter(new ModuleParameterMetadata(title, description, mode, typeof(T), legacy), defaultName));
    }

    /// <summary>
    /// Specifies a list of settings to group together in the UI
    /// </summary>
    /// <param name="title">The title of the group</param>
    /// <param name="lookups">The settings lookups to put in this group</param>
    protected void CreateGroup(string title, params Enum[] lookups)
    {
        Groups.Add(title, lookups.Select(lookup => lookup.ToLookup()).ToList());
    }

    /// <summary>
    /// Allows you to create custom module settings to be listed in the module
    /// </summary>
    protected void CreateCustom(Enum lookup, ModuleSetting moduleSetting)
    {
        validateSettingsLookup(lookup);
        Settings.Add(lookup.ToLookup(), moduleSetting);
    }

    protected void CreateToggle(Enum lookup, string title, string description, bool defaultValue)
    {
        validateSettingsLookup(lookup);
        Settings.Add(lookup.ToLookup(), new BoolModuleSetting(new ModuleSettingMetadata(title, description, typeof(ToggleSettingPage)), defaultValue));
    }

    protected void CreateTextBox(Enum lookup, string title, string description, string defaultValue)
    {
        validateSettingsLookup(lookup);
        Settings.Add(lookup.ToLookup(), new StringModuleSetting(new ModuleSettingMetadata(title, description, typeof(TextBoxSettingPage)), defaultValue));
    }

    protected void CreateTextBox(Enum lookup, string title, string description, int defaultValue)
    {
        validateSettingsLookup(lookup);
        Settings.Add(lookup.ToLookup(), new IntModuleSetting(new ModuleSettingMetadata(title, description, typeof(TextBoxSettingPage)), defaultValue));
    }

    protected void CreateTextBox(Enum lookup, string title, string description, float defaultValue)
    {
        validateSettingsLookup(lookup);
        Settings.Add(lookup.ToLookup(), new FloatModuleSetting(new ModuleSettingMetadata(title, description, typeof(TextBoxSettingPage)), defaultValue));
    }

    protected void CreateSlider(Enum lookup, string title, string description, int defaultValue, int minValue, int maxValue, int tickFrequency = 1)
    {
        validateSettingsLookup(lookup);
        Settings.Add(lookup.ToLookup(), new SliderModuleSetting(new ModuleSettingMetadata(title, description, typeof(SliderSettingPage)), defaultValue, minValue, maxValue, tickFrequency));
    }

    protected void CreateSlider(Enum lookup, string title, string description, float defaultValue, float minValue, float maxValue, float tickFrequency = 0.1f)
    {
        validateSettingsLookup(lookup);
        Settings.Add(lookup.ToLookup(), new SliderModuleSetting(new ModuleSettingMetadata(title, description, typeof(SliderSettingPage)), defaultValue, minValue, maxValue, tickFrequency));
    }

    protected void CreateDropdown<T>(Enum lookup, string title, string description, T defaultValue) where T : Enum
    {
        validateSettingsLookup(lookup);
        Settings.Add(lookup.ToLookup(), new EnumModuleSetting(new ModuleSettingMetadata(title, description, typeof(DropdownSettingPage)), Convert.ToInt32(defaultValue), typeof(T)));
    }

    protected void CreateDateTime(Enum lookup, string title, string description, DateTimeOffset defaultValue)
    {
        validateSettingsLookup(lookup);
        Settings.Add(lookup.ToLookup(), new DateTimeModuleSetting(new ModuleSettingMetadata(title, description, typeof(DateTimeSettingPage)), defaultValue));
    }

    protected void CreateTextBoxList(Enum lookup, string title, string description, IEnumerable<string> defaultValues)
    {
        validateSettingsLookup(lookup);
        Settings.Add(lookup.ToLookup(), new StringListModuleSetting(new ModuleSettingMetadata(title, description, typeof(ListTextBoxSettingPage)), defaultValues));
    }

    protected void CreateTextBoxList(Enum lookup, string title, string description, IEnumerable<int> defaultValues)
    {
        validateSettingsLookup(lookup);
        Settings.Add(lookup.ToLookup(), new IntListModuleSetting(new ModuleSettingMetadata(title, description, typeof(ListTextBoxSettingPage)), defaultValues));
    }

    protected void CreateTextBoxList(Enum lookup, string title, string description, IEnumerable<float> defaultValues)
    {
        validateSettingsLookup(lookup);
        Settings.Add(lookup.ToLookup(), new FloatListModuleSetting(new ModuleSettingMetadata(title, description, typeof(ListTextBoxSettingPage)), defaultValues));
    }

    protected void CreateKeyValuePairList(Enum lookup, string title, string description, IEnumerable<MutableKeyValuePair> defaultValues, string keyTitle, string valueTitle)
    {
        validateSettingsLookup(lookup);
        Settings.Add(lookup.ToLookup(), new MutableKeyValuePairListModuleSetting(new MutableKeyValuePairSettingMetadata(title, description, typeof(MutableKeyValuePairSettingPage), keyTitle, valueTitle), defaultValues));
    }

    private void validateSettingsLookup(Enum lookup)
    {
        if (!Settings.ContainsKey(lookup.ToLookup())) return;

        ExceptionHandler.Handle(new InvalidOperationException($"{FullID} attempted to add an already existing lookup ({lookup.ToLookup()}) to its settings"));
    }

    #region ChatBox

    #region Runtime

    protected void ChangeState(Enum lookup)
    {
        ChangeState(lookup.ToLookup());
    }

    protected void ChangeState(string lookup)
    {
        if (GetState(lookup) is null) throw new InvalidOperationException($"State with lookup {lookup} does not exist");

        ChatBoxManager.GetInstance().ChangeStateTo(FullID, lookup);
    }

    protected void TriggerEvent(Enum lookup)
    {
        TriggerEvent(lookup.ToLookup());
    }

    protected void TriggerEvent(string lookup)
    {
        if (GetEvent(lookup) is null) throw new InvalidOperationException($"Event with lookup {lookup} does not exist");

        ChatBoxManager.GetInstance().TriggerEvent(FullID, lookup);
    }

    protected void SetVariableValue<T>(Enum lookup, T value)
    {
        SetVariableValue(lookup.ToLookup(), value);
    }

    protected void SetVariableValue<T>(string lookup, T value)
    {
        var variable = GetVariable(lookup);
        if (variable is null) throw new InvalidOperationException($"Variable with lookup {lookup} does not exist");

        variable.SetValue(value);
    }

    #endregion

    #region States

    protected ClipStateReference? CreateState(Enum lookup, string displayName, string defaultFormat = "", IEnumerable<ClipVariableReference>? defaultVariables = null, bool defaultShowTyping = false)
    {
        return CreateState(lookup.ToLookup(), displayName, defaultFormat, defaultVariables, defaultShowTyping);
    }

    protected ClipStateReference? CreateState(string lookup, string displayName, string defaultFormat = "", IEnumerable<ClipVariableReference>? defaultVariables = null, bool defaultShowTyping = false)
    {
        if (GetState(lookup) is not null)
        {
            ExceptionHandler.Handle($"[{FullID}]: You cannot add the same lookup ({lookup}) for a state more than once");
            return null;
        }

        var clipStateReference = new ClipStateReference
        {
            ModuleID = FullID,
            StateID = lookup,
            DefaultFormat = defaultFormat,
            DefaultShowTyping = defaultShowTyping,
            DefaultVariables = defaultVariables?.ToList() ?? new List<ClipVariableReference>(),
            DisplayName = { Value = displayName }
        };

        ChatBoxManager.GetInstance().CreateState(clipStateReference);
        return clipStateReference;
    }

    protected void DeleteState(Enum lookup)
    {
        DeleteState(lookup.ToLookup());
    }

    protected void DeleteState(string lookup)
    {
        ChatBoxManager.GetInstance().DeleteState(FullID, lookup);
    }

    protected ClipStateReference? GetState(Enum lookup)
    {
        return GetState(lookup.ToLookup());
    }

    protected ClipStateReference? GetState(string lookup)
    {
        return ChatBoxManager.GetInstance().GetState(FullID, lookup);
    }

    #endregion

    #region Events

    protected ClipEventReference? CreateEvent(Enum lookup, string displayName, string defaultFormat = "", IEnumerable<ClipVariableReference>? defaultVariables = null, bool defaultShowTyping = false, float defaultLength = 5, ClipEventBehaviour defaultBehaviour = ClipEventBehaviour.Override)
    {
        return CreateEvent(lookup.ToLookup(), displayName, defaultFormat, defaultVariables, defaultShowTyping, defaultLength, defaultBehaviour);
    }

    protected ClipEventReference? CreateEvent(string lookup, string displayName, string defaultFormat = "", IEnumerable<ClipVariableReference>? defaultVariables = null, bool defaultShowTyping = false, float defaultLength = 5, ClipEventBehaviour defaultBehaviour = ClipEventBehaviour.Override)
    {
        if (GetEvent(lookup) is not null)
        {
            ExceptionHandler.Handle($"[{FullID}]: You cannot add the same lookup ({lookup}) for an event more than once");
            return null;
        }

        var clipEventReference = new ClipEventReference
        {
            ModuleID = FullID,
            EventID = lookup,
            DefaultFormat = defaultFormat,
            DefaultShowTyping = defaultShowTyping,
            DefaultVariables = defaultVariables?.ToList() ?? new List<ClipVariableReference>(),
            DefaultLength = defaultLength,
            DefaultBehaviour = defaultBehaviour,
            DisplayName = { Value = displayName }
        };

        ChatBoxManager.GetInstance().CreateEvent(clipEventReference);
        return clipEventReference;
    }

    protected void DeleteEvent(Enum lookup)
    {
        DeleteEvent(lookup.ToLookup());
    }

    protected void DeleteEvent(string lookup)
    {
        ChatBoxManager.GetInstance().DeleteEvent(FullID, lookup);
    }

    protected ClipEventReference? GetEvent(Enum lookup)
    {
        return GetEvent(lookup.ToLookup());
    }

    protected ClipEventReference? GetEvent(string lookup)
    {
        return ChatBoxManager.GetInstance().GetEvent(FullID, lookup);
    }

    #endregion

    #region Variables

    /// <summary>
    /// Creates a variable using the specified <paramref name="lookup"/>
    /// </summary>
    /// <param name="lookup">The lookup to retrieve this variable</param>
    /// <param name="displayName">The display name to show the user</param>
    /// <typeparam name="T">The type of this variable's value</typeparam>
    /// <remarks><paramref name="lookup"/> is turned into a string internally, and is only an enum to allow for easier referencing in your code</remarks>
    protected ClipVariableReference? CreateVariable<T>(Enum lookup, string displayName)
    {
        return CreateVariable<T>(lookup.ToLookup(), displayName);
    }

    /// <summary>
    /// Creates a variable using the specified <paramref name="lookup"/>
    /// </summary>
    /// <param name="lookup">The lookup to retrieve this variable</param>
    /// <param name="displayName">The display name to show the user</param>
    /// <typeparam name="T">The type of this variable's value</typeparam>
    protected ClipVariableReference? CreateVariable<T>(string lookup, string displayName)
    {
        Type? clipVariableType = null;

        if (typeof(T) == typeof(bool))
            clipVariableType = typeof(BoolClipVariable);
        else if (typeof(T) == typeof(int))
            clipVariableType = typeof(IntClipVariable);
        else if (typeof(T) == typeof(float))
            clipVariableType = typeof(FloatClipVariable);
        else if (typeof(T) == typeof(string))
            clipVariableType = typeof(StringClipVariable);
        else if (typeof(T) == typeof(DateTimeOffset))
            clipVariableType = typeof(DateTimeClipVariable);
        else if (typeof(T) == typeof(TimeSpan))
            clipVariableType = typeof(TimeSpanClipVariable);

        if (clipVariableType is null)
            throw new InvalidOperationException("No clip variable exists for that type. Request it is added to the SDK or make a custom clip variable");

        return CreateVariable<T>(lookup, displayName, clipVariableType);
    }

    /// <summary>
    /// Creates a variable using the specified <paramref name="lookup"/> and a custom <see cref="ClipVariable"/>
    /// </summary>
    /// <param name="lookup">The lookup to retrieve this variable</param>
    /// <param name="displayName">The display name to show the user</param>
    /// <param name="clipVariableType">The type of <see cref="ClipVariable"/> to create when instancing this variable</param>
    /// <typeparam name="T">The type of this variable's value</typeparam>
    /// <remarks><paramref name="lookup"/> is turned into a string internally, and is only an enum to allow for easier referencing in your code</remarks>
    protected ClipVariableReference? CreateVariable<T>(Enum lookup, string displayName, Type clipVariableType)
    {
        return CreateVariable<T>(lookup.ToLookup(), displayName, clipVariableType);
    }

    /// <summary>
    /// Creates a variable using the specified <paramref name="lookup"/> and a custom <see cref="ClipVariable"/>
    /// </summary>
    /// <param name="lookup">The lookup to retrieve this variable</param>
    /// <param name="displayName">The display name to show the user</param>
    /// <param name="clipVariableType">The type of <see cref="ClipVariable"/> to create when instancing this variable</param>
    /// <typeparam name="T">The type of this variable's value</typeparam>
    protected ClipVariableReference? CreateVariable<T>(string lookup, string displayName, Type clipVariableType)
    {
        if (GetVariable(lookup) is not null)
        {
            ExceptionHandler.Handle($"[{FullID}]: You cannot add the same lookup ({lookup}) for a variable more than once");
            return null;
        }

        var clipVariableReference = new ClipVariableReference
        {
            ModuleID = FullID,
            VariableID = lookup,
            ClipVariableType = clipVariableType,
            ValueType = typeof(T),
            DisplayName = { Value = displayName }
        };

        ChatBoxManager.GetInstance().CreateVariable(clipVariableReference);
        return clipVariableReference;
    }

    /// <summary>
    /// Allows for deleting a variable at runtime.
    /// This is most useful for when you have variables whose existence is reliant on module settings
    /// and you need to delete the variable when the setting disappears
    /// </summary>
    /// <param name="lookup">The lookup to retrieve this variable</param>
    /// <remarks><paramref name="lookup"/> is turned into a string internally, and is only an enum to allow for easier referencing in your code</remarks>
    protected void DeleteVariable(Enum lookup)
    {
        DeleteVariable(lookup.ToLookup());
    }

    /// <summary>
    /// Allows for deleting a variable at runtime.
    /// This is most useful for when you have variables whose existence is reliant on module settings
    /// and you need to delete the variable when the setting disappears
    /// </summary>
    /// <param name="lookup">The lookup to retrieve this variable</param>
    protected void DeleteVariable(string lookup)
    {
        ChatBoxManager.GetInstance().DeleteVariable(FullID, lookup);
    }

    /// <summary>
    /// Retrieves the <see cref="ClipVariableReference"/> using the <paramref name="lookup"/> provided
    /// </summary>
    /// <param name="lookup">The lookup to retrieve this variable</param>
    /// <remarks><paramref name="lookup"/> is turned into a string internally, and is only an enum to allow for easier referencing in your code</remarks>
    protected ClipVariableReference? GetVariable(Enum lookup)
    {
        return GetVariable(lookup.ToLookup());
    }

    /// <summary>
    /// Retrieves the <see cref="ClipVariableReference"/> using the <paramref name="lookup"/> provided
    /// </summary>
    /// <param name="lookup">The lookup to retrieve this variable</param>
    protected ClipVariableReference? GetVariable(string lookup)
    {
        return ChatBoxManager.GetInstance().GetVariable(FullID, lookup);
    }

    #endregion

    #endregion

    /// <summary>
    /// Allows you to set the page that shows up in the `runtime` tab of the run screen
    /// </summary>
    /// <param name="page">This should be the instance of your page</param>
    /// <remarks>You are required to manage your runtime page based on what your module is doing. If you want to remove the runtime page, pass null</remarks>
    protected void SetRuntimePage(Page? page)
    {
        RuntimePage = page;
    }

    /// <summary>
    /// Maps a value <paramref name="source"/> from a source range to a destination range
    /// </summary>
    protected static float Map(float source, float sMin, float sMax, float dMin, float dMax) => dMin + (dMax - dMin) * ((source - sMin) / (sMax - sMin));

    /// <summary>
    /// Allows you to send any parameter name and value.
    /// If you want the user to be able to customise the parameter, register a parameter and use <see cref="SendParameter(Enum,object)"/>
    /// </summary>
    /// <param name="name">The name of the parameter</param>
    /// <param name="value">The value to set the parameter to</param>
    protected void SendParameter(string name, object value)
    {
        AppManager.GetInstance().VRChatOscClient.SendValue($"{VRChatOscConstants.ADDRESS_AVATAR_PARAMETERS_PREFIX}{name}", value);
    }

    /// <summary>
    /// Allows you to send a customisable parameter using its lookup and a value
    /// </summary>
    /// <param name="lookup">The lookup of the parameter</param>
    /// <param name="value">The value to set the parameter to</param>
    protected void SendParameter(Enum lookup, object value)
    {
        if (!Parameters.TryGetValue(lookup, out var moduleParameter))
        {
            ExceptionHandler.Handle(new InvalidOperationException($"Lookup `{lookup}` has not been registered. Please register it by calling RegisterParameter in OnPreLoad"));
            return;
        }

        if (string.IsNullOrWhiteSpace(moduleParameter.Name.Value)) return;

        SendParameter(moduleParameter.Name.Value!, value);
    }

    /// <summary>
    /// Retrieves the container of the setting using the provided lookup. This allows for creating more complex UI callback behaviour.
    /// This is best used inside of <see cref="OnPostLoad"/>
    /// </summary>
    /// <param name="lookup">The lookup of the setting</param>
    /// <returns>The container if successful, otherwise pushes an exception and returns default</returns>
    protected ModuleSetting? GetSetting(Enum lookup) => GetSetting<ModuleSetting>(lookup);

    /// <summary>
    /// Retrieves the container of the setting using the provided lookup and type param for custom <see cref="ModuleSetting"/>s. This allows for creating more complex UI callback behaviour.
    /// This is best used inside of <see cref="OnPostLoad"/>
    /// </summary>
    /// <typeparam name="T">The custom <see cref="ModuleSetting"/> type</typeparam>
    /// <param name="lookup">The lookup of the setting</param>
    /// <returns>The container if successful, otherwise pushes an exception and returns default</returns>
    protected T? GetSetting<T>(Enum lookup) where T : ModuleSetting => GetSetting<T>(lookup.ToLookup());

    internal T? GetSetting<T>(string lookup) where T : ModuleSetting
    {
        lock (loadLock)
        {
            if (Settings.TryGetValue(lookup, out var setting)) return (T)setting;

            return default;
        }
    }

    internal ModuleParameter? GetParameter(string lookup)
    {
        lock (loadLock)
        {
            return Parameters.SingleOrDefault(pair => pair.Key.ToLookup() == lookup).Value;
        }
    }

    /// <summary>
    /// Retrieves a <see cref="ModuleSetting"/>'s value as a shorthand for <see cref="ModuleAttribute.GetValue{TValueType}"/>
    /// </summary>
    /// <param name="lookup">The lookup of the setting</param>
    /// <typeparam name="T">The value type of the setting</typeparam>
    /// <returns>The value if successful, otherwise pushes an exception and returns default</returns>
    protected T? GetSettingValue<T>(Enum lookup)
    {
        lock (loadLock)
        {
            if (!Settings.ContainsKey(lookup.ToLookup())) return default;

            return Settings[lookup.ToLookup()].GetValue<T>(out var value) ? value : default;
        }
    }

    internal void OnParameterReceived(VRChatOscMessage message)
    {
        lock (loadLock)
        {
            if (message.IsAvatarChangeEvent)
            {
                invokeAvatarChange();
                return;
            }

            var receivedParameter = new ReceivedParameter(message.ParameterName, message.ParameterValue);

            try
            {
                OnAnyParameterReceived(receivedParameter);
            }
            catch (Exception e)
            {
                ExceptionHandler.Handle(e, $"Module {FullID} experienced an exception calling {nameof(OnAnyParameterReceived)}");
            }

            var parameterName = Parameters.Values.FirstOrDefault(moduleParameter => parameterNameRegex[moduleParameter.Name.Value!].IsMatch(receivedParameter.Name))?.Name.Value;
            if (parameterName is null) return;

            if (!parameterNameEnum.TryGetValue(parameterName, out var lookup)) return;

            var parameterData = Parameters[lookup];

            if (!parameterData.Metadata.Mode.HasFlagFast(ParameterMode.Read)) return;

            if (!receivedParameter.IsValueType(parameterData.Metadata.Type))
            {
                Log($"Cannot accept input parameter. `{lookup}` expects type `{parameterData.Metadata.Type.ToReadableName()}` but received type `{receivedParameter.Value.GetType().ToReadableName()}`");
                return;
            }

            var registeredParameter = new RegisteredParameter(receivedParameter, lookup, parameterData);

            try
            {
                OnRegisteredParameterReceived(registeredParameter);
            }
            catch (Exception e)
            {
                ExceptionHandler.Handle(e, $"Module {FullID} experienced an exception calling {nameof(OnRegisteredParameterReceived)}");
            }
        }
    }

    private void invokeAvatarChange()
    {
        try
        {
            OnAvatarChange();
        }
        catch (Exception e)
        {
            ExceptionHandler.Handle(e, $"{FullID} has experienced an exception calling {nameof(OnAvatarChange)}");
        }
    }

    internal void InvokePlayerUpdate()
    {
        try
        {
            OnPlayerUpdate();
        }
        catch (Exception e)
        {
            ExceptionHandler.Handle(e, $"{FullID} has experienced an exception calling {nameof(OnPlayerUpdate)}");
        }
    }

    protected virtual void OnAnyParameterReceived(ReceivedParameter receivedParameter)
    {
    }

    protected virtual void OnRegisteredParameterReceived(RegisteredParameter parameter)
    {
    }

    protected virtual void OnAvatarChange()
    {
    }

    protected virtual void OnPlayerUpdate()
    {
    }

    #endregion
}
