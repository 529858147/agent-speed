using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Provider;
using AgentSpeed.Interop;

namespace AgentSpeed.Boards;

/// <summary>
/// UI Automation for a board window (DESIGN 2c Accessibility): the window reads as a list named <c>a11y.board</c> whose
/// items are the group rows (<c>a11y.board.row</c>, bounded by the row on screen, their help text the row's score card while
/// the board's <c>intelligence</c> readout is on: DESIGN 12, <i>Accessibility</i>), followed by the board's actions as
/// invokable buttons. A new leader is announced politely through a notification. Calls arrive on the UI thread.
/// </summary>
internal sealed class BoardAutomation : IRawElementProviderSimple, IRawElementProviderFragment, IRawElementProviderFragmentRoot
{
    private const int ActionIdBase = 1_000_000;

    private readonly Func<nint> _hwnd;
    private readonly Func<string, Rect> _rowBounds;
    private readonly List<Element> _children = [];
    private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);
    private int _items;
    private int _nextId = 1;
    private string _name = string.Empty;

    /// <param name="rowBounds">A row's rectangle in screen pixels (empty when it is not shown).</param>
    public BoardAutomation(Func<nint> hwnd, Func<string, Rect> rowBounds)
    {
        _hwnd = hwnd;
        _rowBounds = rowBounds;
    }

    /// <summary>A group row's help text by its key (its score card's full description), read when a client asks for it.</summary>
    public Func<string, string>? RowHelp { get; set; }

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            var old = _name;
            _name = value;
            if (AutomationInteropProvider.ClientsAreListening && _hwnd() != 0)
            {
                AutomationInteropProvider.RaiseAutomationPropertyChangedEvent(this,
                    new AutomationPropertyChangedEventArgs(AutomationElementIdentifiers.NameProperty, old, value));
            }
        }
    }

    /// <summary>The group rows in rank order (key, label); the actions stay after them.</summary>
    public void SetRows(IReadOnlyList<(string Key, string Name)> rows)
    {
        var actions = _children.Skip(_items).ToArray();
        _children.Clear();
        foreach (var (key, name) in rows)
        {
            if (!_ids.TryGetValue(key, out var id))
            {
                id = _nextId++;
                _ids[key] = id;
            }

            _children.Add(new Element(this, id, key, name, ControlType.ListItem, null));
        }

        _items = rows.Count;
        _children.AddRange(actions);
        if (_ids.Count > 64)
        {
            var live = rows.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var key in _ids.Keys.Where(k => !live.Contains(k)).ToArray())
            {
                _ids.Remove(key);
            }
        }
    }

    public void SetActions(IReadOnlyList<(string Name, Action Invoke)> actions)
    {
        _children.RemoveRange(_items, _children.Count - _items);
        for (var i = 0; i < actions.Count; i++)
        {
            _children.Add(new Element(this, ActionIdBase + i, null, actions[i].Name, ControlType.Button, actions[i].Invoke));
        }
    }

    /// <summary>Reads <paramref name="text"/> once, politely (<c>a11y.board.newLeader</c>).</summary>
    public void Announce(string text)
    {
        if (string.IsNullOrEmpty(text) || _hwnd() == 0 || !AutomationInteropProvider.ClientsAreListening)
        {
            return;
        }

        AutomationInteropProvider.RaiseAutomationEvent(AutomationElementIdentifiers.NotificationEvent, this,
            new NotificationEventArgs(AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent, text, "boardNewLeader"));
    }

    public ProviderOptions ProviderOptions => ProviderOptions.ServerSideProvider | ProviderOptions.UseComThreading;

    public IRawElementProviderSimple? HostRawElementProvider => _hwnd() is var hwnd and not 0 ? AutomationInteropProvider.HostProviderFromHandle(hwnd) : null;

    public object? GetPatternProvider(int patternId) => null;

    public object? GetPropertyValue(int propertyId)
    {
        if (propertyId == AutomationElementIdentifiers.NameProperty.Id)
        {
            return _name;
        }

        if (propertyId == AutomationElementIdentifiers.ControlTypeProperty.Id)
        {
            return ControlType.List.Id;
        }

        if (propertyId == AutomationElementIdentifiers.ClassNameProperty.Id)
        {
            return "AgentSpeedBoard";
        }

        if (propertyId == AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id)
        {
            return false;
        }

        return null;
    }

    public Rect BoundingRectangle => Rect.Empty;

    public IRawElementProviderFragmentRoot FragmentRoot => this;

    public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;

    public int[]? GetRuntimeId() => null;

    public IRawElementProviderFragment? Navigate(NavigateDirection direction) => direction switch
    {
        NavigateDirection.FirstChild => _children.Count > 0 ? _children[0] : null,
        NavigateDirection.LastChild => _children.Count > 0 ? _children[^1] : null,
        _ => null,
    };

    public void SetFocus()
    {
    }

    public IRawElementProviderFragment? ElementProviderFromPoint(double x, double y)
    {
        for (var i = 0; i < _items && i < _children.Count; i++)
        {
            if (_children[i].BoundingRectangle.Contains(x, y))
            {
                return _children[i];
            }
        }

        return this;
    }

    public IRawElementProviderFragment? GetFocus() => null;

    private Rect CenterPixel()
    {
        var hwnd = _hwnd();
        if (hwnd == 0 || !User32.GetWindowRect(hwnd, out var rect))
        {
            return Rect.Empty;
        }

        return new Rect((rect.Left + rect.Right) / 2.0, (rect.Top + rect.Bottom) / 2.0, 1, 1);
    }

    /// <summary>A group row (list item) or a board action (invokable button).</summary>
    private sealed class Element(BoardAutomation root, int id, string? key, string name, ControlType type, Action? invoke)
        : IRawElementProviderSimple, IRawElementProviderFragment, IInvokeProvider
    {
        public ProviderOptions ProviderOptions => ProviderOptions.ServerSideProvider | ProviderOptions.UseComThreading;

        public IRawElementProviderSimple? HostRawElementProvider => null;

        public object? GetPatternProvider(int patternId) => invoke is not null && patternId == InvokePatternIdentifiers.Pattern.Id ? this : null;

        public object? GetPropertyValue(int propertyId)
        {
            if (propertyId == AutomationElementIdentifiers.NameProperty.Id)
            {
                return name;
            }

            if (propertyId == AutomationElementIdentifiers.ControlTypeProperty.Id)
            {
                return type.Id;
            }

            if (propertyId == AutomationElementIdentifiers.HelpTextProperty.Id)
            {
                return key is not null && root.RowHelp?.Invoke(key) is { Length: > 0 } help ? help : null;
            }

            if (propertyId == AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id)
            {
                return false;
            }

            if (propertyId == AutomationElementIdentifiers.IsInvokePatternAvailableProperty.Id)
            {
                return invoke is not null;
            }

            return null;
        }

        public Rect BoundingRectangle => key is not null ? root._rowBounds(key) : root.CenterPixel();

        public IRawElementProviderFragmentRoot FragmentRoot => root;

        public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;

        public int[] GetRuntimeId() => [AutomationInteropProvider.AppendRuntimeId, id];

        public IRawElementProviderFragment? Navigate(NavigateDirection direction)
        {
            var index = root._children.IndexOf(this);
            return direction switch
            {
                NavigateDirection.Parent => root,
                NavigateDirection.NextSibling => index >= 0 && index + 1 < root._children.Count ? root._children[index + 1] : null,
                NavigateDirection.PreviousSibling => index > 0 ? root._children[index - 1] : null,
                _ => null,
            };
        }

        public void SetFocus()
        {
        }

        public void Invoke()
        {
            if (invoke is { } action)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(action);
            }
        }
    }
}
