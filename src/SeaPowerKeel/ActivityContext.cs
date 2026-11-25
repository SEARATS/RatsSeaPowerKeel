using System;

namespace SeaPowerKeel;

// Diagnostics must never break the mod that calls them: a blank owner or an unknown kind gives an empty scope, no exception.
public readonly struct PluginId
{
    public string Value { get; }
    public PluginId(string value) => Value = string.IsNullOrWhiteSpace(value) ? null : value;
    public bool IsEmpty => Value == null;
    public override string ToString() => Value ?? "";
}

public enum ActivityKind { Other, UiInput, SettingsEdit, SettingsRebuild, GameAction }

public class ActivityContext
{
    public PluginId Owner { get; }
    public ActivityKind Kind { get; }
    public string Detail { get; }

    public ActivityContext(PluginId owner, ActivityKind kind, params string[] details)
    {
        Owner = owner;
        Kind = Enum.IsDefined(typeof(ActivityKind), kind) ? kind : ActivityKind.Other;
        Detail = string.Join(" ", details ?? Array.Empty<string>());
    }
}

/// <summary>A diagnostic owner bound once, so individual operations need no plugin ID.</summary>
public class PluginActivity
{
    public PluginId Owner { get; }
    public PluginActivity(PluginId owner) => Owner = owner;

    public IDisposable BeginActivity(ActivityKind operation, params string[] details) =>
        Keel.TrackActivity(new ActivityContext(Owner, operation, details));

    // A mod's own enum says more than any ActivityKind: recorded as Other, with its name leading the detail.
    public IDisposable BeginActivity<TOperation>(TOperation operation, params string[] details) where TOperation : struct, Enum =>
        BeginActivity(ActivityKind.Other, $"{typeof(TOperation).Name}.{operation}", string.Join(" ", details ?? Array.Empty<string>()));
}
