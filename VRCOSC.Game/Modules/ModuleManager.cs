﻿// Copyright (c) VolcanicArts. Licensed under the GPL-3.0 License.
// See the LICENSE file in the repository root for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using osu.Framework.Extensions.IEnumerableExtensions;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Timing;
using VRCOSC.Game.Modules.Serialisation;
using VRCOSC.Game.OSC.VRChat;
using VRCOSC.Game.SDK;
using VRCOSC.Game.Serialisation;
using Module = VRCOSC.Game.SDK.Module;

namespace VRCOSC.Game.Modules;

public class ModuleManager
{
    private readonly GameHost host;
    private readonly Storage storage;
    private readonly IClock clock;
    private readonly AppManager appManager;

    private AssemblyLoadContext? localModulesContext;
    private Dictionary<string, AssemblyLoadContext>? remoteModulesContexts;

    public readonly Dictionary<Assembly, List<Module>> LocalModules = new();
    public readonly Dictionary<Assembly, List<Module>> RemoteModules = new();

    private IEnumerable<Module> modules => LocalModules.Values.SelectMany(moduleList => moduleList).Concat(RemoteModules.Values.SelectMany(moduleList => moduleList)).ToList();
    private IEnumerable<Module> runningModules => modules.Where(module => module.State.Value == ModuleState.Started);

    public ModuleManager(GameHost host, Storage storage, IClock clock, AppManager appManager)
    {
        this.host = host;
        this.storage = storage;
        this.clock = clock;
        this.appManager = appManager;
    }

    #region Runtime

    public async Task StartAsync()
    {
        var enabledModules = modules.Where(module => module.Enabled.Value).ToList();
        foreach (var module in enabledModules) await module.Start();
    }

    public async Task StopAsync()
    {
        foreach (var module in runningModules) await module.Stop();
    }

    public void FrameworkUpdate()
    {
        runningModules.ForEach(module => module.FrameworkUpdate());
    }

    public void PlayerUpdate()
    {
        runningModules.ForEach(module => module.PlayerUpdate());
    }

    public void AvatarChange()
    {
        runningModules.ForEach(module => module.AvatarChange());
    }

    public void ParameterReceived(VRChatOscMessage vrChatOscMessage)
    {
        runningModules.ForEach(module => module.OnParameterReceived(vrChatOscMessage));
    }

    #endregion

    #region Management

    /// <summary>
    /// Reloads all local and remote modules by unloading their assembly contexts and calling <see cref="LoadAllModules"/>
    /// </summary>
    public void ReloadAllModules()
    {
        Logger.Log("Module reload requested");

        UnloadAllModules();
        LoadAllModules();
    }

    public void UnloadAllModules()
    {
        Logger.Log("Unloading all modules");

        LocalModules.Clear();
        RemoteModules.Clear();

        localModulesContext = null;
        remoteModulesContexts = null;

        Logger.Log("Unloading successful");
    }

    /// <summary>
    /// Loads all local and remote modules
    /// </summary>
    public void LoadAllModules()
    {
        try
        {
            loadLocalModules();
            loadRemoteModules();

            modules.ForEach(module =>
            {
                var moduleSerialisationManager = new SerialisationManager();
                moduleSerialisationManager.RegisterSerialiser(1, new ModuleSerialiser(storage, module, appManager.ProfileManager.ActiveProfile));

                module.InjectDependencies(host, clock, appManager, moduleSerialisationManager);
                module.Load();
            });
        }
        catch (Exception)
        {
            // TODO: Handle this
        }
    }

    private void loadLocalModules()
    {
        Logger.Log("Loading local modules");

        if (localModulesContext is not null)
            throw new InvalidOperationException("Cannot load local modules while local modules are already loaded");

        var localModulesPath = storage.GetStorageForDirectory("packages/local").GetFullPath(string.Empty, true);
        localModulesContext = loadContextFromPath(localModulesPath);
        Logger.Log($"Found {localModulesContext.Assemblies.Count()} assemblies");

        if (!localModulesContext.Assemblies.Any()) return;

        var localModules = retrieveModuleInstances("local", localModulesContext);

        localModules.ForEach(localModule =>
        {
            if (!LocalModules.ContainsKey(localModule.GetType().Assembly))
            {
                LocalModules[localModule.GetType().Assembly] = new List<Module>();
            }

            LocalModules[localModule.GetType().Assembly].Add(localModule);
        });

        Logger.Log($"Final local module count: {localModules.Count}");
    }

    private void loadRemoteModules()
    {
        Logger.Log("Loading remote modules");

        if (remoteModulesContexts is not null)
            throw new InvalidOperationException("Cannot load remote modules while remote modules are already loaded");

        remoteModulesContexts = new Dictionary<string, AssemblyLoadContext>();

        var remoteModulesDirectory = storage.GetStorageForDirectory("packages/remote").GetFullPath(string.Empty, true);
        Directory.GetDirectories(remoteModulesDirectory).ForEach(moduleDirectory =>
        {
            var packageId = moduleDirectory.Split('\\').Last();
            remoteModulesContexts.Add(packageId, loadContextFromPath(moduleDirectory));
        });
        Logger.Log($"Found {remoteModulesContexts.Values.Sum(remoteModuleContext => remoteModuleContext.Assemblies.Count())} assemblies");

        if (!remoteModulesContexts.Any()) return;

        var remoteModules = new List<Module>();
        remoteModulesContexts.ForEach(pair => remoteModules.AddRange(retrieveModuleInstances(pair.Key, pair.Value)));

        remoteModules.ForEach(remoteModule =>
        {
            if (!RemoteModules.ContainsKey(remoteModule.GetType().Assembly))
            {
                RemoteModules[remoteModule.GetType().Assembly] = new List<Module>();
            }

            RemoteModules[remoteModule.GetType().Assembly].Add(remoteModule);
        });

        Logger.Log($"Final remote module count: {remoteModules.Count}");
    }

    private List<Module> retrieveModuleInstances(string packageId, AssemblyLoadContext assemblyLoadContext)
    {
        var moduleInstanceList = new List<Module>();

        try
        {
            assemblyLoadContext.Assemblies.ForEach(assembly => moduleInstanceList.AddRange(assembly.ExportedTypes.Where(type => type.IsSubclassOf(typeof(Module)) && !type.IsAbstract).Select(type =>
            {
                var module = (Module)Activator.CreateInstance(type)!;
                module.PackageId = packageId;
                return module;
            })));
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error in ModuleManager");
        }

        return moduleInstanceList;
    }

    private AssemblyLoadContext loadContextFromPath(string path)
    {
        var assemblyLoadContext = new AssemblyLoadContext(null);
        Directory.GetFiles(path, "*.dll").ForEach(dllPath => loadAssemblyFromPath(assemblyLoadContext, dllPath));
        return assemblyLoadContext;
    }

    private void loadAssemblyFromPath(AssemblyLoadContext context, string path)
    {
        try
        {
            using var assemblyStream = new FileStream(path, FileMode.Open);
            context.LoadFromStream(assemblyStream);
        }
        catch (Exception e)
        {
            Logger.Error(e, "ModuleManager experienced an exception");
        }
    }

    #endregion
}
