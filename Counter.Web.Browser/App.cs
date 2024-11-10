using System;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Threading.Tasks;

public partial class App
{

    private static Model _state;
    private static Node? _node;
    private static Application<Model, Command, object> _application = new Application<Model, Command, object>
            {
                JsonTypeInfo = SerializationContext.Default.Command,
                InitialState = new Model(0),
                Delta = Differences.Delta<Command>(),
                Apply = Browser.DOM.Apply(SerializationContext.Default.Command),
                Update =
                    static (model, command) => 
                        command switch
                        {
                            Increment => (model with { Counter = model.Counter + 1 }, []),
                            Decrement => (model with { Counter = model.Counter - 1 }, []),
                            _ => (model, Array.Empty<object>())
                        },
                View = static model =>
                    div(new Id(0), [], [
                        
                            button(new Id(1), 
                                [
                                    new Property { Name = "type", Value = "button" },
                                    onclick(new Increment())
                                ],
                                [
                                    text(new Id(2), "+")
                                ]),
                            button(new Id(3), 
                                [
                                    new Property { Name = "type", Value = "button" },
                                    onclick(new Decrement())                            
                                ], 
                                [
                                    text(new Id(4), "-")
                                ])
                            ]
                            
                    )
            };

    [JSExport]
    public static async Task Dispatch(string serializedCommand)
    {       
        var command = JsonSerializer.Deserialize(serializedCommand, _application.JsonTypeInfo);
        if (command == null)
        {
            throw new Exception("Failed to deserialize command");
        }
        if(_node == null)
        {
            throw new Exception("Node is null");
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

    [JSExport]

    public static async Task Start()
    {
        if (_application == null)
        {
            throw new Exception("Application is null");
        }
        if (_application.InitialState == null)
        {
            throw new Exception("Initial state is null");
        }
        if (_application.View == null)
        {
            throw new Exception("View is null");
        }
        _state = _application.InitialState ;
        _node = _application.View(_state) ;

        // Render initial view to HTML
        string html = Render.Html(_node, _application.JsonTypeInfo);

        // Set the HTML content of the 'app' div
        await Browser.DOM.SetAppContent(html);
    }
}


