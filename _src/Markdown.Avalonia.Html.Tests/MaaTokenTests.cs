using MFAAvalonia.Extensions.MaaFW;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class MaaTokenTests
{
    [Fact]
    public void 合并同一自定义动作的参数时应保留不同字段()
    {
        var token = new MaaToken();
        token.Merge(new Dictionary<string, JToken>
        {
            ["FB_IsSwordSelect"] = JObject.Parse("""
                { "action": { "type": "Custom", "custom_action": "FlowerBrushSwordSelectionAction", "custom_action_param": { "type_short": true } } }
                """)
        });
        token.Merge(new Dictionary<string, JToken>
        {
            ["FB_IsSwordSelect"] = JObject.Parse("""
                { "action": { "custom_action_param": { "max_swipes": 500 } } }
                """)
        });

        var layers = JArray.Parse(token.ToString()!);
        var finalParameters = (JObject)layers[1]!["FB_IsSwordSelect"]!["action"]!["custom_action_param"]!;

        Assert.True((bool)finalParameters["type_short"]!);
        Assert.Equal(500, (int)finalParameters["max_swipes"]!);
    }

    [Fact]
    public void 合并同名参数时应以后续值覆盖()
    {
        var token = new MaaToken();
        token.Merge(new Dictionary<string, JToken>
        {
            ["Action"] = JObject.Parse("""
                { "action": { "type": "Custom", "custom_action": "Example", "custom_action_param": { "value": 1 } } }
                """)
        });
        token.Merge(new Dictionary<string, JToken>
        {
            ["Action"] = JObject.Parse("""
                { "action": { "custom_action_param": { "value": 2 } } }
                """)
        });

        var layers = JArray.Parse(token.ToString()!);
        var value = (int)layers[1]!["Action"]!["action"]!["custom_action_param"]!["value"]!;

        Assert.Equal(2, value);
    }
}
