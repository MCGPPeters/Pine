using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Pine;

public interface Component : Node;

public abstract record Component<TComponent, TState, TCommand> : Component
{
    private readonly ConcurrentDictionary<string, TCommand> _handlers = new ConcurrentDictionary<string, TCommand>();

    public Node VirtualDom { get; private set; }

    protected abstract TState Update(TState state, TCommand command);
    public abstract Node View(TState state);

    public required string Tag { get; init; } = nameof(TComponent).ToLower();

    public required string Id { get; init; }
    public TState State { get; private set;}

    public Component(TState initialState)
    {
        State = initialState;
        var newVirtualDom = View(State);
        var patches = Delta(VirtualDom, newVirtualDom);
        foreach (var patch in patches)
        {
            Apply(patch).Wait();
        }
        VirtualDom = newVirtualDom;
    }

    public static List<Patch> Delta(Node oldNode, Node newNode)
    {
        List<Patch> patches = (oldNode, newNode) switch
        {       
            (Text o, Text n) => o.Content == n.Content
                ? []
                : [new UpdateText{ Node = o, Text = n.Content }],
            (Element o, Element n) when o.Tag != n.Tag => [new ReplaceChild{ OldElement = o, NewElement = n }],
            (Element o, Element n) => DeltaElements(o, n),
            (Component<TComponent, TState, TCommand> o, Component<TComponent, TState, TCommand> n) => Delta(o.VirtualDom, n.VirtualDom),
            (Component<TComponent, TState, TCommand> o, Element n) => Delta(o.VirtualDom, n),
            (Element o, Component<TComponent, TState, TCommand> n) => Delta(o, n.VirtualDom),

            _ => throw new Exception("Unknown node type")
        };
        return patches;
    }

    public async Task Apply(Patch patch)
    {
        switch (patch)
        {
            case AddChild addChild:
                var childHtml = Html(addChild.Child);
                await Browser.DOM.AddChildHtml( addChild.Parent.Id, childHtml);
                break;

            case RemoveChild removeChild:
                await Browser.DOM.RemoveChild(removeChild.Parent.Id, removeChild.Child.Id);
                break;

            case ReplaceChild replace:
                var newHtml = Html(replace.NewElement);
                Console.WriteLine(replace.NewElement.Tag);
                await Browser.DOM.ReplaceChildHtml(replace.OldElement.Id, newHtml);
                break;

            case UpdateText updateText:
                await Browser.DOM.UpdateTextContent(updateText.Node.Id, updateText.Text);
                break;

            case UpdateProperty updateProperty:
                await Browser.DOM.UpdateAttribute(updateProperty.Element.Id, updateProperty.Attribute.Name, updateProperty.Value);
                break;

            case AddProperty addProperty:
                var addProp = addProperty.Attribute;
                await Browser.DOM.AddAttribute(addProperty.Element.Id, addProp.Name, addProp.Value);
                break;

            case RemoveProperty removeProperty:
                var remProp = removeProperty.Attribute;
                await Browser.DOM.RemoveAttribute(removeProperty.Element.Id, remProp.Name);
                break;

            case RemoveHandler removeHandler:
                var remHandler = removeHandler.Handler;

                await Browser.DOM.RemoveAttribute(removeHandler.Element.Id, $"data-event-{remHandler.Name}");
                break;

            case AddHandler<TCommand> addHandler:
                Console.WriteLine("Adding handler");
                var handler = addHandler.Handler;
                var commandId = Guid.NewGuid().ToString();
                if(!_handlers.TryAdd(commandId, handler.Command)) throw new InvalidOperationException("Command already exists");
                await Browser.DOM.AddAttribute(addHandler.Element.Id, $"data-event-{handler.Name}", commandId);
                break;

            default:
                throw new InvalidOperationException("Unsupported patch type");
        }
    }

    public async Task Dispatch(string commandId)
    {
        if (!_handlers.TryGetValue(commandId, out var command))
        {
            throw new InvalidOperationException("Command not found");
        }
        
        var newNode = View(State);
        var patches = Delta(VirtualDom, newNode);
        foreach (var patch in patches)
        {
            await Apply(patch);
        }
        VirtualDom = newNode;
        State = Update(State, command);      
    }
    
    private static List<Patch> DeltaElements(Element oldElement, Element newElement)
    {
        var patches = new List<Patch>();

        if (oldElement.Tag != newElement.Tag)
        {
            return [new ReplaceChild{ OldElement = oldElement, NewElement = newElement }];
        }
        foreach (var attribute in newElement.Attributes)
        {
            if (attribute is Property property)
            {
                var matchingOldProperty = oldElement.Attributes
                    .OfType<Property>()
                    .FirstOrDefault(p => p.Name == property.Name);

                bool exists = oldElement.Attributes
                    .OfType<Property>()
                    .Any(p => p.Name == property.Name);

                if (exists)
                {
                    if (matchingOldProperty.Value != property.Value)
                    {
                        patches.Add(new UpdateProperty
                        {
                            Element = oldElement,
                            Attribute = property,
                            Value = property.Value
                        });
                    }
                }
                else
                {
                    patches.Add(new AddProperty
                    {
                        Element = oldElement,
                        Attribute = property
                    });
                }
            }

            if(attribute is Handler<TCommand> handler)
            {
                var matchingOldHandler = oldElement.Attributes
                    .OfType<Handler>()
                    .FirstOrDefault(p => p.Name == handler.Name);

                bool exists = oldElement.Attributes
                    .OfType<Handler>()
                    .Any(p => p.Name == handler.Name);

                if (exists && matchingOldHandler != null)
                {
                    if (!Equals(matchingOldHandler.Id, handler.Id))
                    {
                        patches.Add(new RemoveHandler
                        {
                            Element = oldElement,
                            Handler = matchingOldHandler
                        });
                        patches.Add(new AddHandler<TCommand>
                        {
                            Element = oldElement,
                            Handler = handler
                        });
                    }
                }
                else
                {
                    patches.Add(new AddHandler<TCommand>
                    {
                        Element = oldElement,
                        Handler = handler
                    });
                }
            }
        }

        foreach (var attribute in oldElement.Attributes)
        {
            if (attribute is Property property)
            {
                bool existsInNew = newElement.Attributes
                    .OfType<Property>()
                    .Any(p => p.Name == property.Name);

                if (!existsInNew)
                {
                    patches.Add(new RemoveProperty
                    {
                        Element = oldElement,
                        Attribute = property
                    });
                }
            }

            if(attribute is Handler<TCommand> handler)
            {
                bool existsInNew = newElement.Attributes
                    .OfType<Handler<TCommand>>()
                    .Any(p => p.Name == handler.Name);

                if (!existsInNew)
                {
                    patches.Add(new RemoveHandler
                    {
                        Element = oldElement,
                        Handler = handler
                    });
                }


            }
        }

        // Compare Children
        int maxChildren = Math.Max(oldElement.Children.Length, newElement.Children.Length);
        for (int i = 0; i < maxChildren; i++)
        {
            var oldChild = i < oldElement.Children.Length ? oldElement.Children[i] : default;
            var newChild = i < newElement.Children.Length ? newElement.Children[i] : default;

            if(oldChild == null)
            {
                if(newChild is Element elementChild)
                {
                    patches.Add(new AddChild{ Parent = oldElement, Child = elementChild });
                }
                continue;
            }

            if(newChild == null)
            {
                patches.Add(new RemoveChild{ Parent = oldElement, Child = (Element)oldChild });
                continue;
            }

            var childPatches = Delta(oldChild, newChild);
            patches.AddRange(childPatches);
        }

        return patches;
    }

    public string Html(Node node) => 
        node switch
        {
            Text text => System.Web.HttpUtility.HtmlEncode(text.Content),
            Element element => RenderElement(element),
            Component<TComponent, TState, TCommand> component => Html(component.View(component.State)),
            _ => throw new Exception("Unknown node type"),
        };

    private string RenderElement(Element element)
    {
        var tag = element.Tag;
        var attributes = RenderAttributes(element.Attributes);
        var children = string.Concat(element.Children.Select(element => Html(element)));

        return $"<{tag}{attributes}>{children}</{tag}>";
    }

    private static string RenderAttributes(IEnumerable<Attribute> attributes)
    {
        var renderedAttributes = new List<string>();

        foreach (var attr in attributes)
        {
            switch (attr)
            {
                case Property prop:
                    renderedAttributes.Add($"{prop.Name}=\"{System.Net.WebUtility.HtmlEncode(prop.Value)}\"");
                    break;

                case Handler handler:
                    renderedAttributes.Add($"data-event-{handler.Name}=\"{handler.Id}\"");
                    break;
            }
        }

        return renderedAttributes.Count > 0 ? " " + string.Join(" ", renderedAttributes) : string.Empty;
    }

}

public readonly record struct Id(int Value)
{
    // implicit conversion to int
    public static implicit operator int(Id id) => id.Value;
}

public interface Node{
    public string Id { get; init; }
}

public interface Attribute;

public readonly record struct Property : Attribute
{
    public required string Name { get; init; }
    public string Value { get; init; }
}

public interface Handler
{
    string Id { get; }

    string Name { get; init; }
}

public record Handler<TCommand> : Attribute, Handler
{
    public string Id { get; } = Guid.NewGuid().ToString();
    public required string Name { get; init; }
    public required TCommand Command { get; init; }
}

public readonly record struct Text : Node
{
    public required string Id { get; init; }
    public required string Content { get; init; }
}

public readonly record struct Element : Node
{
    public required string Id { get; init; }
    public string Tag { get; init; }
    public Node[] Children { get; init; }
    public Attribute[] Attributes { get; init; }
}

public abstract record Option<T>;
public sealed record Some<T>(T Value) : Option<T>;
public sealed record None<T> : Option<T>;

public interface Protocol;

public readonly record struct Http : Protocol;

public readonly record struct Https : Protocol;
public readonly record struct Url
{
    public required Protocol Protocol { get; init; }
    public required string Host { get; init; }

    public required Option<int> Port { get; init; }
    public required string Path { get; init; }
    public required Option<string> Query { get; init; }
    public required Option<string> Fragment { get; init; }
}


public readonly ref struct ParseResult<T>
{
    public T Value { get; }
    public ReadOnlySpan<char> Remaining { get; }
    public bool Success { get; }

    private ParseResult(T value, ReadOnlySpan<char> remaining)
    {
        Value = value;
        Remaining = remaining;
        Success = true;
    }

    public static ParseResult<T> Successful(T value, ReadOnlySpan<char> remaining)
        => new(value, remaining);

    public static ParseResult<T> Failure => default;
}

public delegate ParseResult<T> Parser<T>(ReadOnlySpan<char> input);

public static class Parse
{
    public static Parser<char> Item() => 
        static input => 
            !input.IsEmpty 
                ? ParseResult<char>.Successful(input[0], input.Slice(1)) 
                : ParseResult<char>.Failure;


    public static Parser<char> Satisfy(Func<char, bool> predicate) =>
        input =>
            !input.IsEmpty && predicate(input[0])
                ? ParseResult<char>.Successful(input[0], input.Slice(1))
                : ParseResult<char>.Failure;

    public static Parser<char> Char(char expected) =>
        input =>
            !input.IsEmpty && input[0] == expected
                ? ParseResult<char>.Successful(expected, input.Slice(1))
                : ParseResult<char>.Failure;

    public static Parser<char> Digit => 
        Satisfy(char.IsDigit);

    public static Parser<char> Letter =>
        Satisfy(char.IsLetter);


    public static Parser<T> Return<T>(T value) => 
        input => 
            ParseResult<T>.Successful(value, input);

    public static Parser<T> Or<T>(this Parser<T> first, Parser<T> second) => 
        input =>
        {
            var result = first(input);
            return result.Success 
                ? result 
                : second(input);
        };

    public static Parser<TResult> Bind<TSource, TResult>(
    this Parser<TSource> parser,
    Func<TSource, Parser<TResult>> binder) => 
        input =>
        {
            var result = parser(input);
            return !result.Success 
                ? ParseResult<TResult>.Failure 
                : binder(result.Value)(result.Remaining);
        };

    public static Parser<U> SelectMany<T, U>(this Parser<T> parser, Func<T, Parser<U>> f) =>
        Bind(parser, f);

    public static Parser<V> SelectMany<T, U, V>(this Parser<T> parser, Func<T, Parser<U>> f, Func<T, U, V> project) =>
        Bind(parser, x => Bind(f(x), y => Return(project(x, y))));
    

    public static Parser<U> Map<T, U>(this Parser<T> parser, Func<T, U> f) =>
        Bind(parser, x => Return(f(x)));

    public static Parser<U> Select<T, U>(this Parser<T> parser, Func<T, U> f) =>
        Map(parser, f);

    /// <summary>
    /// Parses the given string.
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    public static Parser<string> String(string expected) => 
        input =>
        {
            var expectedSpan = expected.AsSpan();
            return input.StartsWith(expectedSpan)
                ? ParseResult<string>.Successful(expected, input.Slice(expectedSpan.Length))
                : ParseResult<string>.Failure;
        };

    public static Parser<List<T>> Many<T>(this Parser<T> parser)
    {
        return input =>
        {
            var results = new List<T>();
            var remainder = input;
            while (true)
            {
                var result = parser(remainder);
                if (!result.Success)
                    break;
                results.Add(result.Value);
                remainder = result.Remaining;
            }
            return ParseResult<List<T>>.Successful(results, remainder);
        };
    }

    public static Parser<List<T>> Many1<T>(this Parser<T> parser)
    {
        return parser.Bind(first =>
            parser.Many().Select(rest =>
            {
                rest.Insert(0, first);
                return rest;
            })
        );
    }


    /// <summary>
    /// Parses an integer.
    /// </summary>
    public static Parser<int> Integer =>
        from digits in Many1(Digit)
        select int.Parse(new string([.. digits]));

    public static Parser<object> EndOfInput = static input =>
        input.IsEmpty 
        ? ParseResult<object>.Successful(new(), input) 
        : ParseResult<object>.Failure;
}

public interface Route;

public abstract record Route<TState> : Route
{
    public required TState State { get; init; }
}

public static class Html
{
    public static Node text(string content, [CallerLineNumber] int id = 0) 
        => new Text{ Id = id.ToString(), Content = content };

    public static Node element(
        string tag,
        Attribute[] attributes,
        Node[] children,
        [CallerLineNumber] int id = 0
    ) => new Element{
        Id = id.ToString(),
        Tag = tag,
        Attributes = attributes,
        Children = children
    };

    public static Node img(
        Attribute[] attributes,
        [CallerLineNumber] int id = 0
    ) => element("img", attributes, Array.Empty<Node>(), id);

    public static Node a(
        Attribute[] attributes,
        Node[] children,
        [CallerLineNumber] int id = 0
    ) => element("a", attributes, children, id);

    public static Node h1(
        Attribute[] attributes,
        Node[] children,
        [CallerLineNumber] int id = 0
    ) => element("h1", attributes, children, id);

    public static Node h2(
        Attribute[] attributes,
        Node[] children,
        [CallerLineNumber] int id = 0
    ) => element("h2", attributes, children, id);

    public static Node h3(
        Attribute[] attributes,
        Node[] children,
        [CallerLineNumber] int id = 0
    ) => element("h3", attributes, children, id);

    public static Node h4(
        Attribute[] attributes,
        Node[] children,
        [CallerLineNumber] int id = 0
    ) => element("h4", attributes, children, id);

    public static Node h5(
        Attribute[] attributes,
        Node[] children,
        [CallerLineNumber] int id = 0
    ) => element("h5", attributes, children, id);

    public static Node h6(
        Attribute[] attributes,
        Node[] children,
        [CallerLineNumber] int id = 0
    ) => element("h6", attributes, children, id);

    public static Node span(
        Attribute[] attributes,
        Node[] children,
        [CallerLineNumber] int id = 0
    ) => element("span", attributes, children, id);

    public static Node div(
        Attribute[] attributes,
        Node[] children,
        [CallerLineNumber] int id = 0
    ) => element("div", attributes, children, id);

    public static Node button(
        Attribute[] attributes,
        Node[] children,
        [CallerLineNumber] int id = 0
    ) => element("button", attributes, children, id);

    public static Node p(
        Attribute[] attributes,
        Node[] children,
        [CallerLineNumber] int id = 0
    ) => element("p", attributes, children, id);

    // Add more elements as needed...
}

public static class Attributes
{
    public static Attribute Property(string name, string value)
        => new Property{ Name = name, Value = value };
    public static Attribute onclick<TCommand>(TCommand message)
        => new Handler<TCommand>{ Name = "onclick", Command = message };

    public static Attribute href(string url)
        => new Property{ Name = "href", Value = url };

    public static Attribute src(string url)
        => new Property{ Name = "src", Value = url };

    

    // Add more attributes as needed...
}

public interface Patch;

public readonly record struct ReplaceChild : Patch
{
    public required Element OldElement { get; init; }
    public required Element NewElement { get; init; }
}

public readonly record struct AddChild : Patch
{
    public required Element Parent { get; init; }
    public required Element Child { get; init; }
}


public readonly record struct RemoveChild : Patch
{
    public required Element Parent { get; init; }
    public required Element Child { get; init; }
}

public readonly record struct UpdateProperty : Patch
{
    public required Element Element { get; init; }
    public required Property Attribute { get; init; }

    public required string Value { get; init; }
}

public readonly record struct AddProperty : Patch
{
    public required Element Element { get; init; }
    public required Property Attribute { get; init; }
}

public readonly record struct RemoveProperty : Patch
{
    public required Element Element { get; init; }
    public required Property Attribute { get; init; }
}

public readonly record struct RemoveHandler : Patch
{
    public required Element Element { get; init; }
    public required Handler Handler { get; init; }
}

public readonly record struct AddHandler<TCommand> : Patch
{
    public required Element Element { get; init; }
    public required Handler<TCommand> Handler { get; init; }
}

public readonly record struct UpdateText : Patch
{
    public required Text Node { get; init; }
    public required string Text { get; init; }
}

public static partial class Browser
{
    public static partial class DOM
    {
        [JSImport("getAppElement", "pine.js")]
        public static partial Task<Element> GetAppElement();

        [JSImport("removeChild", "pine.js")]
        public static partial Task RemoveChild(string parentId, string childId);

        [JSImport("addChildHtml", "pine.js")]
        public static partial Task AddChildHtml(string parentId, string childHtml);

        [JSImport("replaceChildHtml", "pine.js")]
        public static partial Task ReplaceChildHtml(string oldNodeId, string newHtml);

        [JSImport("updateTextContent", "pine.js")]
        public static partial Task UpdateTextContent(string nodeId, string newText);

        [JSImport("updateAttribute", "pine.js")]
        public static partial Task UpdateAttribute(string id, string name, string value);

        [JSImport("addAttribute", "pine.js")]
        public static partial Task AddAttribute(string id, string name, string value);

        [JSImport("removeAttribute", "pine.js")]
        public static partial Task RemoveAttribute(string id, string name);

        [JSImport("setAppContent", "pine.js")]
        public static partial Task SetAppContent(string html);
    }
}