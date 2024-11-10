using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.ComponentModel;

namespace Pine;

public delegate (TState newState, TEvent[] events) Update<TState, TCommand, TEvent>(TState state, TCommand command);

public delegate Node View<TState, TCommand>(TState state);

public readonly record struct Id(int Value)
{
    // implicit conversion to int
    public static implicit operator int(Id id) => id.Value;
}

public interface Node{
    public Id Id { get; init; }
}

public interface Attribute;

public readonly record struct Property : Attribute
{
    public required string Name { get; init; }
    public string Value { get; init; }
}

public readonly record struct Handler<TCommand> : Attribute
{
    public required string Name { get; init; }
    public required TCommand Command { get; init; }
}

public readonly record struct Text : Node
{
    public required Id Id { get; init; }
    public required string Content { get; init; }
}

public readonly record struct Element : Node
{
    public required Id Id { get; init; }
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


public static class Render
{
    public static string Html<TCommand>(Node node, JsonTypeInfo<TCommand> jsonTypeInfo) => 
        node switch
        {
            Text text => System.Web.HttpUtility.HtmlEncode(text.Content),
            Element element => RenderElement(element, jsonTypeInfo),
            _ => throw new Exception("Unknown node type"),
        };

    private static string RenderElement<TCommand>(Element element, JsonTypeInfo<TCommand> jsonTypeInfo)
    {
        var tag = element.Tag;
        var attributes = RenderAttributes(element.Attributes, jsonTypeInfo);
        var children = string.Concat(element.Children.Select(element => Html(element, jsonTypeInfo)));

        return $"<{tag}{attributes}>{children}</{tag}>";
    }

    private static string RenderAttributes<TCommand>(IEnumerable<Attribute> attributes, JsonTypeInfo<TCommand> jsonTypeInfo)
    {
        var renderedAttributes = new List<string>();

        foreach (var attr in attributes)
        {
            Console.WriteLine($"Attribute: {attr.GetType().Name}");
            switch (attr)
            {
                case Property prop:
                    Console.WriteLine($"Property: {prop.Name}={prop.Value}");
                    renderedAttributes.Add($"{prop.Name}=\"{System.Net.WebUtility.HtmlEncode(prop.Value)}\"");
                    break;

                case Handler<TCommand> handler:
                    Console.WriteLine($"Handler: {handler.Name}");
                    // Add a data attribute to hold the event information
                    var serializedCommand = JsonSerializer.Serialize(handler.Command, jsonTypeInfo);
                    renderedAttributes.Add($"data-event-{handler.Name}=\"{serializedCommand}\"");
                    break;
            }
        }

        return renderedAttributes.Count > 0 ? " " + string.Join(" ", renderedAttributes) : string.Empty;
    }
}

public static class Html
{
    public static Node text(Id id, string content) 
        => new Text{ Id = id, Content = content };

    public static Node element(
        Id id,
        string tag,
        Attribute[] attributes,
        Node[] children
    ) => new Element{
        Id = id,
        Tag = tag,
        Attributes = attributes,
        Children = children
    };

    public static Node div(
        Id id,
        Attribute[] attributes,
        Node[] children
    ) => element(id, "div", attributes, children);

    public static Node button(
        Id id,
        Attribute[] attributes,
        Node[] children
    ) => element(id, "button", attributes, children);

    public static Node p(
        Id id,
        Attribute[] attributes,
        Node[] children
    ) => element(id, "p", attributes, children);

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

public delegate List<Patch> Delta(Node oldNode, Node newNode);

public delegate Task<Object> Apply(Patch patch);

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

public readonly record struct RemoveHandler<TCommand> : Patch
{
    public required Element Element { get; init; }
    public required Handler<TCommand> Handler { get; init; }
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
        [JSImport("removeChild", "pine.js")]
        public static partial Task RemoveChild(int parentId, int childId);

        [JSImport("addChildHtml", "pine.js")]
        public static partial Task AddChildHtml(int parentId, string childHtml);

        [JSImport("replaceChildHtml", "pine.js")]
        public static partial Task ReplaceChildHtml(int oldNodeId, string newHtml);

        [JSImport("updateTextContent", "pine.js")]
        public static partial Task UpdateTextContent(int nodeId, string newText);

        [JSImport("updateAttribute", "pine.js")]
        public static partial Task UpdateAttribute(int id, string name, string value);

        [JSImport("addAttribute", "pine.js")]
        public static partial Task AddAttribute(int id, string name, string value);

        [JSImport("removeAttribute", "pine.js")]
        public static partial Task RemoveAttribute(int id, string name);

        [JSImport("setAppContent", "pine.js")]
        public static partial Task SetAppContent(string html);

        public static Apply Apply<TCommand>(JsonTypeInfo<TCommand> jsonTypeInfo) => async patch =>
        {
            Console.WriteLine($"Applying patch: {patch}");
            switch (patch)
            {
                case AddChild addChild:
                    var childHtml = Render.Html(addChild.Child, jsonTypeInfo);
                    await AddChildHtml( addChild.Parent.Id.Value, childHtml);
                    break;

                case RemoveChild removeChild:
                    await RemoveChild(removeChild.Parent.Id.Value, removeChild.Child.Id.Value);
                    break;

                case ReplaceChild replace:
                    var newHtml = Render.Html(replace.NewElement, jsonTypeInfo);
                    await ReplaceChildHtml(replace.OldElement.Id.Value, newHtml);
                    break;

                case UpdateText updateText:
                    await UpdateTextContent(updateText.Node.Id.Value, updateText.Text);
                    break;

                case UpdateProperty updateProperty:
                    await UpdateAttribute(updateProperty.Element.Id.Value, updateProperty.Attribute.Name, updateProperty.Value);
                    break;

                case AddProperty addProperty:
                    var addProp = addProperty.Attribute;
                    Console.WriteLine($"AddProperty: {addProp.Name}={addProp.Value}");
                    await AddAttribute(addProperty.Element.Id.Value, addProp.Name, addProp.Value);
                    break;

                case RemoveProperty removeProperty:
                    var remProp = removeProperty.Attribute;
                    await RemoveAttribute(removeProperty.Element.Id.Value, remProp.Name);
                    break;

                case RemoveHandler<TCommand> removeHandler:
                    var remHandler = removeHandler.Handler;
                    await RemoveAttribute(removeHandler.Element.Id.Value, $"data-event-{remHandler.Name}");
                    break;

                case AddHandler<TCommand> addHandler:
                    var handler = addHandler.Handler;
                    var serializedCommand = JsonSerializer.Serialize(handler.Command, jsonTypeInfo);
                    await AddAttribute(addHandler.Element.Id.Value, $"data-event-{handler.Name}", serializedCommand);
                    break;

                default:
                    throw new InvalidOperationException("Unsupported patch type");
            }

            return new ();
        };
    }
}


public static class Differences
{

    public static Delta Delta<TCommand>() => (Node oldNode, Node newNode) =>
    {
        List<Patch> patches = (oldNode, newNode) switch
        {
            
            (Text o, Text n) => o.Content == n.Content
                ? []
                : [new UpdateText{ Node = o, Text = n.Content }],
            (Element o, Element n) when o.Tag != n.Tag => [new ReplaceChild{ OldElement = o, NewElement = n }],
            (Element o, Element n) => DeltaElements<TCommand>(o, n),   
            _ => throw new Exception("Unknown node type")
        };
        return patches;
    };

    private static List<Patch> DeltaElements<TCommand>(Element oldElement, Element newElement)
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
                    .OfType<Handler<TCommand>>()
                    .FirstOrDefault(p => p.Name == handler.Name);

                bool exists = oldElement.Attributes
                    .OfType<Handler<TCommand>>()
                    .Any(p => p.Name == handler.Name);

                if (exists)
                {
                    if (!Equals(matchingOldHandler.Command, handler.Command))
                    {
                        patches.Add(new RemoveHandler<TCommand>
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
                    patches.Add(new RemoveHandler<TCommand>
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

            var childPatches = Delta<TCommand>()(oldChild, newChild);
            patches.AddRange(childPatches);
        }

        return patches;
    }
    
}

public record Application<TState, TCommand, TEvent> : PineApplication
{
    public required JsonTypeInfo<TCommand> JsonTypeInfo { get; init; }
    public required Delta Delta { get; init; }

    public required Apply Apply { get; init; }

    public required TState InitialState { get; init; }

    public required Update<TState, TCommand, object> Update { get; init; }

    public required View<TState, TCommand> View { get; init; }      
}

public interface PineApplication;



public class PineApplication<TState, TCommand, TEvent>
{
    private static Application<TState, TCommand, TEvent>? _application;
    private static TState? _state;
    private static Node? _node;

    // Initialize the application
    public static async Task Run(Application<TState, TCommand, TEvent> application)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _state = application.InitialState ?? throw new ArgumentNullException(nameof(application.InitialState));
        _node = application.View(_state) ?? throw new ArgumentNullException(nameof(application.View));

        // Render initial view to HTML
        string html = Render.Html(_node, application.JsonTypeInfo);

        // Set the HTML content of the 'app' div
        await Browser.DOM.SetAppContent(html);
    }
    public async Task Dispatch(string serializedCommand)
    {       
        if (_application is null) throw new InvalidOperationException("Application is not initialized");
        if (_state is null) throw new InvalidOperationException("State is not initialized");
        if (_node is null) throw new InvalidOperationException("Node is not initialized");

        var command = JsonSerializer.Deserialize(serializedCommand, _application.JsonTypeInfo);
        if (command == null)
        {
            throw new Exception("Failed to deserialize command");
        }

        // Run your update logic
        var newNode = _application.View(_state);
        var patches = _application.Delta(_node, newNode);
        foreach (var patch in patches)
        {
            await _application.Apply(patch);
        }
        var (newState, _) = _application.Update(_state, command);
        _state = newState;
        _node = newNode;
        
    }
}