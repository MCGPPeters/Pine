using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using static Pine.Browser;

namespace Pine
{

    public abstract record Application<TComponent, TState, TCommand> where TCommand : class, Command
    {
        private readonly ConcurrentDictionary<string, TCommand> _handlers = new();

        public Node VirtualDom { get; private set; } = div([], []);

        protected abstract TState Update(TState state, TCommand command);
        public abstract Node View(TState state);

        public required string Id { get; init; }
        public TState State { get; protected set; }

        protected Application(TState initialState)
        {
            State = initialState;
            // Asynchronous initialization moved to InitializeAsync
        }

        public async Task InitializeAsync()
        {
            VirtualDom = View(State);

            // Register event handlers before rendering
            RegisterEventHandlers(VirtualDom);

            var html = Html(VirtualDom);
            await DOM.SetAppContent(html);
            
        }

        private void RegisterEventHandlers(Node node)
        {
            switch (node)
            {
                case Element element:
                    // Register handlers in the current element
                    foreach (var attribute in element.Attributes)
                    {
                        if (attribute is Handler handler)
                        {
                            if(handler.Command is TCommand typedCommand)
                                if (!_handlers.TryAdd(handler.Id, typedCommand))
                                {
                                    throw new InvalidOperationException("Command already exists");
                                }
                        }

                    }

                    // Recursively register handlers in child nodes
                    foreach (var child in element.Children)
                    {
                        RegisterEventHandlers(child);
                    }
                    break;

                case Application<TComponent, TState, TCommand> component:
                    // Register handlers in child components
                    RegisterEventHandlers(component.VirtualDom);
                    break;

                case Text:
                    // Text nodes don't have event handlers
                    break;
            }
        }

        public static List<Patch> Delta(Node oldNode, Node newNode)
        {
            if (oldNode is null)
            {
                // Generate patches to create the newNode from scratch
                return [new ReplaceChild { OldElement = new Element { Id = "", Tag = "", Attributes = [], Children = [] }, NewElement = (Element)newNode }];
            }

            List<Patch> patches = (oldNode, newNode) switch
            {
                (Text o, Text n) => o.Content == n.Content
                    ? []
                    : [new UpdateText { Node = o, Text = n.Content }],
                (Element o, Element n) when o.Tag != n.Tag => new List<Patch> { new ReplaceChild { OldElement = o, NewElement = n } },
                (Element o, Element n) => DeltaElements(o, n),
                (Element o, Application<TComponent, TState, TCommand> n) => Delta(o, n.VirtualDom),
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
                    await Browser.DOM.AddChildHtml(addChild.Parent.Id, childHtml);
                    break;

                case RemoveChild removeChild:
                    await Browser.DOM.RemoveChild(removeChild.Parent.Id, removeChild.Child.Id);
                    break;

                case ReplaceChild replace:
                    // matches when OldElement is not null
                    if (replace.OldElement is { } oldElement)
                    {
                        var newHtml = Html(replace.NewElement);
                        await Browser.DOM.ReplaceChildHtml(oldElement.Id, newHtml);
                    }
                    else
                    {
                        // Initial rendering: set the app content
                        var newHtml = Html(replace.NewElement);
                        await Browser.DOM.SetAppContent(newHtml);
                    }
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

                case AddHandler addHandler:
                    var handler = addHandler.Handler;
                    if(handler.Command is TCommand typedCommand)
                        if (!_handlers.TryAdd(handler.Id, typedCommand))
                        throw new InvalidOperationException("Command already exists");
                    await Browser.DOM.AddAttribute(addHandler.Element.Id, $"data-event-{handler.Name}", handler.Id);
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

            // Update the state
            State = Update(State, command);

            Console.WriteLine($"State updated: {State}");

            // Generate new virtual DOM
            var newVirtualDom = View(State);

            Console.WriteLine($"New virtual DOM: {newVirtualDom}");

            // Compute the patches
            var patches = Delta(VirtualDom, newVirtualDom);

            Console.WriteLine($"Patches: {patches}");

            // Apply patches
            foreach (var patch in patches)
            {
                await Apply(patch);
            }

            Console.WriteLine("Patches applied");

            // Update the current virtual DOM
            VirtualDom = newVirtualDom;
        }

        private static List<Patch> DeltaElements(Element oldElement, Element newElement)
        {
            var patches = new List<Patch>();

            if (oldElement.Tag != newElement.Tag)
            {
                return [new ReplaceChild { OldElement = oldElement, NewElement = newElement }];
            }

            // Compare Attributes
            var oldAttributes = oldElement.Attributes.ToDictionary(a => a switch
            {
                Property p => p.Name,
                Handler h => h.Name,
                _ => throw new Exception("Unknown attribute type")
            });

            var newAttributes = newElement.Attributes.ToDictionary(a => a switch
            {
                Property p => p.Name,
                Handler h => h.Name,
                _ => throw new Exception("Unknown attribute type")
            });

            foreach (var attr in newAttributes)
            {
                if (oldAttributes.TryGetValue(attr.Key, out var oldAttr))
                {
                    if (!attr.Value.Equals(oldAttr))
                    {
                        if (attr.Value is Property prop)
                        {
                            patches.Add(new UpdateProperty
                            {
                                Element = oldElement,
                                Attribute = prop,
                                Value = prop.Value
                            });
                        }
                        else if (attr.Value is Handler handler)
                        {
                            patches.Add(new RemoveHandler
                            {
                                Element = oldElement,
                                Handler = (Handler)oldAttr
                            });
                            patches.Add(new AddHandler
                            {
                                Element = oldElement,
                                Handler = handler
                            });
                        }
                    }
                }
                else
                {
                    if (attr.Value is Property prop)
                    {
                        patches.Add(new AddProperty
                        {
                            Element = oldElement,
                            Attribute = prop
                        });
                    }
                    else if (attr.Value is Handler handler)
                    {
                        patches.Add(new AddHandler
                        {
                            Element = oldElement,
                            Handler = handler
                        });
                    }
                }
            }

            foreach (var attr in oldAttributes)
            {
                if (!newAttributes.ContainsKey(attr.Key))
                {
                    if (attr.Value is Property prop)
                    {
                        patches.Add(new RemoveProperty
                        {
                            Element = oldElement,
                            Attribute = prop
                        });
                    }
                    else if (attr.Value is Handler handler)
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
                var oldChild = i < oldElement.Children.Length ? oldElement.Children[i] : null;
                var newChild = i < newElement.Children.Length ? newElement.Children[i] : null;

                if (oldChild == null && newChild != null)
                {
                    if (newChild is Element elementChild)
                    {
                        patches.Add(new AddChild { Parent = oldElement, Child = elementChild });
                    }
                    continue;
                }

                if (newChild == null && oldChild != null)
                {
                    patches.Add(new RemoveChild { Parent = oldElement, Child = (Element)oldChild });
                    continue;
                }

                if (oldChild != null && newChild != null)
                {
                    var childPatches = Delta(oldChild, newChild);
                    patches.AddRange(childPatches);
                }
            }

            return patches;
        }

        public string Html(Node node) =>
            node switch
            {
                Text text => $"<span id=\"{text.Id}\">{System.Net.WebUtility.HtmlEncode(text.Content)}</span>",
                Element element => RenderElement(element),
                _ => throw new Exception("Unknown node type"),
            };

        private string RenderElement(Element element)
        {
            var tag = element.Tag;
            var attributes = RenderAttributes(element.Attributes);
            var children = string.Concat(element.Children.Select(child => Html(child)));

            return $"<{tag} id=\"{element.Id}\" {attributes}>{children}</{tag}>";
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

    public interface Node
    {
        public string Id { get; init; }
    }

    public interface Attribute { }

    public readonly record struct Property : Attribute
    {
        public required string Name { get; init; }
        public required string Value { get; init; }
    }

    public interface Command;

    public record Handler : Attribute
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public required string Name { get; init; }

        public required Command Command { get; init; }
    }

    public readonly record struct Text : Node
    {
        public required string Id { get; init; }
        public required string Content { get; init; }
    }

    public readonly record struct Element : Node
    {
        public required string Id { get; init; }
        public required string Tag { get; init; }
        public required Node[] Children { get; init; }
        public required Attribute[] Attributes { get; init; }
    }

    public interface Patch { }

    public readonly record struct ReplaceChild : Patch
    {
        public required Element? OldElement { get; init; }
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

    public readonly record struct AddHandler : Patch
    {
        public required Element Element { get; init; }
        public required Handler Handler { get; init; }
    }

    public readonly record struct UpdateText : Patch
    {
        public required Text Node { get; init; }
        public required string Text { get; init; }
    }

    public static class Html
    {
        public static Node text(string content, [CallerLineNumber] int id = 0) =>
            new Text { Id = id.ToString(), Content = content };

        public static Node element(
            string tag,
            Attribute[] attributes,
            Node[] children,
            [CallerLineNumber] int id = 0
        ) => new Element
        {
            Id = id.ToString(),
            Tag = tag,
            Attributes = attributes,
            Children = children
        };

        public static Node button(
            Attribute[] attributes,
            Node[] children,
            [CallerLineNumber] int id = 0
        ) => element("button", attributes, children, id);

        public static Node div(
            Attribute[] attributes,
            Node[] children,
            [CallerLineNumber] int id = 0
        ) => element("div", attributes, children, id);

        // Add more elements as needed...
    }

    public static class ButtonAttributes
    {
        public static Attribute type(string value) =>
            new Property { Name = "type", Value = value };

        public static Attribute onclick(Command command) =>
            new Handler { Name = "click", Command = command };
    }

    public static class Attributes
    {
        public static Attribute Property(string name, string value) =>
            new Property { Name = name, Value = value };

        public static Attribute onclick(Command command) =>
            new Handler { Name = "onclick", Command = command };

        // Add more attributes as needed...
    }

    public static partial class Browser
    {
        public static partial class DOM
        {
            [JSImport("setAppContent", "pine.js")]
            public static partial Task SetAppContent(string html);

            [JSImport("addChildHtml", "pine.js")]
            public static partial Task AddChildHtml(string parentId, string childHtml);

            [JSImport("removeChild", "pine.js")]
            public static partial Task RemoveChild(string parentId, string childId);

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
        }

        

        
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


}
