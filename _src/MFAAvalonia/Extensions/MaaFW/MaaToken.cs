using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW;

public class MaaToken
{
    private List<Dictionary<string, JToken>> Tokens = [];


    public void Merge(Dictionary<string, JToken> token)
    {
        var clonedToken = CloneTokenDictionary(token);
        foreach (var (nodeName, nodeValue) in clonedToken)
        {
            if (nodeValue is not JObject nodeObject
                || nodeObject["action"] is not JObject actionObject
                || actionObject["custom_action_param"] is not JObject actionParameters)
                continue;

            var mergedParameters = new JObject();
            string? currentActionName = null;
            foreach (var previousToken in Tokens)
            {
                if (previousToken.TryGetValue(nodeName, out var previousNode)
                    && previousNode is JObject previousNodeObject
                    && previousNodeObject["action"] is JObject previousActionObject)
                {
                    var previousActionName = (string?)previousActionObject["custom_action"];
                    if (previousActionName != null && currentActionName != previousActionName)
                    {
                        mergedParameters.RemoveAll();
                        currentActionName = previousActionName;
                    }

                    if (previousActionObject["custom_action_param"] is JObject previousParameters)
                    {
                        if (currentActionName == null)
                            currentActionName = previousActionName;
                        mergedParameters.Merge(previousParameters, new JsonMergeSettings
                        {
                            MergeArrayHandling = MergeArrayHandling.Replace,
                            MergeNullValueHandling = MergeNullValueHandling.Ignore
                        });
                    }
                }
            }

            var incomingActionName = (string?)actionObject["custom_action"];
            if (incomingActionName != null && currentActionName != null && currentActionName != incomingActionName)
                mergedParameters.RemoveAll();

            mergedParameters.Merge(actionParameters, new JsonMergeSettings
            {
                MergeArrayHandling = MergeArrayHandling.Replace,
                MergeNullValueHandling = MergeNullValueHandling.Ignore
            });
            actionObject["custom_action_param"] = mergedParameters;
        }

        Tokens.Add(clonedToken);
    }

    public static MaaToken FromDictionary(Dictionary<string, JToken> token)
    {
        MaaToken result = new MaaToken();
        result.Merge(token);
        return result;
    }

    public override string ToString()
    {
        var settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        };
        return JsonConvert.SerializeObject(Tokens, settings);
    }

    private static Dictionary<string, JToken> CloneTokenDictionary(Dictionary<string, JToken> token)
    {
        return token.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.DeepClone());
    }
}
