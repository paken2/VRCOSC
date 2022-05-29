﻿// Copyright (c) VolcanicArts. Licensed under the GPL-3.0 License.
// See the LICENSE file in the repository root for full license text.

using Newtonsoft.Json;

namespace VRCOSC.Game.Modules.Modules.HypeRate.Models;

public class EventModel
{
    [JsonProperty("event")]
    public string Event = null!;
}
