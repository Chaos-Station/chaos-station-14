// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Security.Cryptography.X509Certificates;

namespace Content.Shared._Chaos.Chemistry.Components;

/// <summary>
/// Component that makes a hypospray/medipen single-use.
/// The entity will be deleted after it's used once.
/// </summary>
[RegisterComponent]
public sealed partial class SingleUseHyposprayComponent : Component
{
    [DataField]
    public bool IsUsed = false;
}
