﻿// Copyright (c) VolcanicArts. Licensed under the GPL-3.0 License.
// See the LICENSE file in the repository root for full license text.

using System.Diagnostics;
using System.Linq;
using VRCOSC.OSC.VRChat;

namespace VRCOSC.SDK.VRChat;

public class VRChatClient
{
    public readonly Player Player;

    private bool lastKnownOpenState;

    public VRChatClient(VRChatOscClient oscClient)
    {
        Player = new Player(oscClient);
    }

    public void Teardown()
    {
        Player.ResetAll();
    }

    public bool HasOpenStateChanged(out bool openState)
    {
        var newOpenState = Process.GetProcessesByName("vrchat").Any();

        if (newOpenState == lastKnownOpenState)
        {
            openState = lastKnownOpenState;
            return false;
        }

        openState = lastKnownOpenState = newOpenState;
        return true;
    }
}
