using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}

namespace PeakSWC.RemoteBlazorWebView.SourceGenerators
{
    internal enum UiFramework { Unknown, Wpf, WinForms }

    internal enum Mode { Runtime = 0, Local = 1, Remote = 2 }

    /// <summary>Everything the generator needs about one [RemoteBlazor] class, as plain values so incremental caching works.</summary>
    internal sealed record Target(
        string? Namespace,
        EquatableArray<string> ContainingTypes,
        string TypeName,
        string WebView,
        UiFramework Framework,
        Mode Mode,
        bool ModeFromAttribute,
        string? ServerUri,
        string? Id,
        bool ShowLink,
        bool Mirror,
        string? ReverseMirror,
        bool ShareControl,
        bool Unattended,
        bool Voice,
        bool RestartOnRefresh,
        bool ShutdownOnDisconnect,
        bool CanEmit,
        LocationInfo? Location,
        EquatableArray<DiagnosticInfo> Diagnostics);

    internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
    {
        public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

        public static LocationInfo? From(Location? location) =>
            location is null || location.SourceTree is null ? null : new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }

    internal sealed record DiagnosticInfo(string Id, LocationInfo? Location, EquatableArray<string> Args)
    {
        public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, Location? location, params string[] args) =>
            new(descriptor.Id, LocationInfo.From(location), new EquatableArray<string>(args.ToImmutableArray()));

        public Diagnostic ToDiagnostic()
        {
            var descriptor = Id switch
            {
                "RBW001" => Diagnostics.MemberNotFound,
                "RBW002" => Diagnostics.NotRemoteBlazorWebView,
                "RBW003" => Diagnostics.NotPartial,
                "RBW004" => Diagnostics.RemoteNeedsServer,
                "RBW005" => Diagnostics.BadServerUri,
                "RBW006" => Diagnostics.BadId,
                "RBW007" => Diagnostics.BadModeProperty,
                "RBW009" => Diagnostics.MirrorAndReverseMirror,
                "RBW010" => Diagnostics.NotWebView2,
                "RBW011" => Diagnostics.ShareControlAndMirror,
                "RBW012" => Diagnostics.VoiceNeedsWpf,
                _ => Diagnostics.ServerUriIgnored,
            };
            return Diagnostic.Create(descriptor, Location?.ToLocation(), Args.ToArray<object>());
        }
    }

    /// <summary>An immutable array compared by its items.</summary>
    internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T> where T : IEquatable<T>
    {
        private readonly ImmutableArray<T> _items;

        public EquatableArray(ImmutableArray<T> items) => _items = items;

        public ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

        public bool Equals(EquatableArray<T> other) => Items.SequenceEqual(other.Items);

        public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

        public override int GetHashCode()
        {
            var hash = 17;
            foreach (var item in Items) hash = unchecked(hash * 31 + (item?.GetHashCode() ?? 0));
            return hash;
        }

        public TOut[] ToArray<TOut>() => Items.Cast<TOut>().ToArray();

        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
