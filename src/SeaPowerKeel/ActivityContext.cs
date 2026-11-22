using System;

namespace SeaPowerKeel;

public readonly struct PluginId
{
    public string Value { get; }
    public PluginId(string value) => Value = !string.IsNullOrWhiteSpace(value)
        ? value : throw new ArgumentException("Supply the owning plugin's ID.", nameof(value));
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
        if (string.IsNullOrWhiteSpace(owner.Value)) throw new ArgumentException("Supply the owning plugin's ID.", nameof(owner));
        if (!Enum.IsDefined(typeof(ActivityKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        Owner = owner;
        Kind = kind;
        Detail = string.Join(" ", details ?? Array.Empty<string>());
    }
}

/// <summary>A diagnostic owner bound once, so individual operations need no plugin ID.</summary>
public class PluginActivity
{
    public PluginId Owner { get; }
    public PluginActivity(PluginId owner) => Owner = !string.IsNullOrWhiteSpace(owner.Value)
        ? owner : throw new ArgumentException("Supply the owning plugin's ID.", nameof(owner));

    public IDisposable BeginActivity(ActivityKind operation, params string[] details) =>
        Keel.TrackActivity(new ActivityContext(Owner, operation, details));

    public IDisposable BeginActivity<TOperation>(TOperation operation, params string[] details) where TOperation : struct, Enum =>
        BeginActivity(ActivityKind.GameAction, $"{typeof(TOperation).Name}.{operation}", string.Join(" ", details ?? Array.Empty<string>()));
}
