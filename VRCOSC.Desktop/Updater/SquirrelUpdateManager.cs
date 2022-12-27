// Copyright (c) VolcanicArts. Licensed under the GPL-3.0 License.
// See the LICENSE file in the repository root for full license text.

using System;
using System.Linq;
using System.Threading.Tasks;
using Squirrel;
using VRCOSC.Game.Graphics.Updater;

namespace VRCOSC.Desktop.Updater;

public partial class SquirrelUpdateManager : VRCOSCUpdateManager
{
    private const string repo = "https://github.com/VolcanicArts/VRCOSC";

    private GithubUpdateManager? updateManager;
    private UpdateInfo? updateInfo;
    private bool useDelta;

    public SquirrelUpdateManager()
    {
        initialise();
    }

    private void initialise()
    {
        updateInfo = null;
        useDelta = true;
    }

    protected override async Task CheckForUpdateAsync()
    {
        Log("Checking for updates");

        updateManager ??= new GithubUpdateManager(repo);

        if (!updateManager.IsInstalledApp)
        {
            Log("Portable app detected. Cancelled update check");
            return;
        }

        try
        {
            var checkNotification = PostCheckNotification();
            updateInfo = await updateManager.CheckForUpdate(!useDelta, p => checkNotification.Progress = p / 100f);

            if (!updateInfo.ReleasesToApply.Any())
            {
                Log("No updates found");
                initialise();
                return;
            }

            Log($"{updateInfo.ReleasesToApply.Count} updates found");

            if (ShouldApplyImmediately())
                await ApplyUpdates();
            else
                PostUpdateAvailableNotification();
        }
        catch (Exception e)
        {
            PostFailNotification();
            LogError(e);
            initialise();
        }
    }

    protected override async Task ApplyUpdates()
    {
        Log("Attempting to apply updates");

        if (updateManager is null || updateInfo is null)
            throw new InvalidOperationException("Cannot apply updates without checking");

        try
        {
            var notification = PostProgressNotification();
            await updateManager.DownloadReleases(updateInfo.ReleasesToApply, p => notification.Progress = map(p / 100f, 0, 1, 0, 0.5f));
            await updateManager.ApplyReleases(updateInfo, p => notification.Progress = map(p / 100f, 0, 1, 0.5f, 1));
            PostSuccessNotification();
            Log("Update successfully applied");
            initialise();
        }
        catch (Exception e)
        {
            // Update may have failed due to the installed version being too outdated
            // Retry without trying for delta
            if (useDelta)
            {
                useDelta = false;
                await CheckForUpdateAsync();
                return;
            }

            PostFailNotification();
            LogError(e);
            initialise();
        }
    }

    protected override void RequestRestart()
    {
        UpdateManager.RestartAppWhenExited().ContinueWith(_ => Exit());
    }

    private static float map(float source, float sMin, float sMax, float dMin, float dMax)
    {
        return dMin + (dMax - dMin) * ((source - sMin) / (sMax - sMin));
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);
        updateManager?.Dispose();
    }
}
