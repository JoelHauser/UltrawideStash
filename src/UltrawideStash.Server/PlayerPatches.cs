using System.Reflection;
using HarmonyLib;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils;

namespace UltrawideStash.Server;

/// <summary>
/// Serves a player whose width differs from the server's their own stash templates over
/// <c>/client/items</c>. Everyone else gets SPT's own response. See <see cref="PlayerStashes"/>.
///
/// ## Two shapes of the same route
///
/// <c>DataCallbacks.GetTemplateItems</c> returns <c>ValueTask&lt;string&gt;</c> in the
/// 4.1.2 package this builds against, and <c>ValueTask&lt;StreamedJsonBody&gt;</c> in
/// 4.1.6, where the body is serialised straight into the response. The type does not
/// exist at compile time, so the answer is built to whichever the running server's
/// method returns, and handed back through Harmony's boxed <c>__result</c>.
/// </summary>
public static class ServeStashPatch
{
    private static PlayerStashes? players;

    /// <summary>Builds the answer for the running server's return type, or null when it is neither.</summary>
    private static Func<object, object>? respond;

    public static MethodBase Target()
        => AccessTools.Method(typeof(DataCallbacks), nameof(DataCallbacks.GetTemplateItems))
           ?? throw new MissingMethodException(nameof(DataCallbacks), nameof(DataCallbacks.GetTemplateItems));

    /// <summary>
    /// Wire up, and work out how to answer. Throws when the route returns something this
    /// does not know, so the patch is never applied rather than applied wrongly.
    /// </summary>
    public static void Use(PlayerStashes stashes, HttpResponseUtil response)
    {
        players = stashes;

        var returns = ((MethodInfo)Target()).ReturnType;

        if (returns == typeof(ValueTask<string>))
        {
            respond = items => new ValueTask<string>(response.GetBody(items));
            return;
        }

        var streamed = typeof(HttpResponseUtil).GetMethod("GetStreamedBody");

        if (returns.IsGenericType
            && returns.GetGenericTypeDefinition() == typeof(ValueTask<>)
            && streamed is { IsGenericMethodDefinition: true }
            && streamed.ReturnType == returns.GetGenericArguments()[0])
        {
            respond = items =>
            {
                var body = streamed.MakeGenericMethod(items.GetType())
                    .Invoke(response, [items, BackendErrorCodes.None, null])!;

                return Activator.CreateInstance(returns, body)!;
            };
            return;
        }

        respond = null;

        throw new NotSupportedException($"/client/items returns {returns}, which this version does not know");
    }

    public static bool Prefix(MongoId sessionID, ref object __result)
    {
#if SIMULATE_REPLACED_ITEMS
        // Test builds only (-p:DefineConstants=SIMULATE_REPLACED_ITEMS): serve the database
        // as it is, the way the Fika host in Forge issue #5 did, so the game's own fix
        // (ClientTemplates) can be seen working in a real game. Never in a release build.
        return true;
#else
        if (respond is null) return true;

        var items = players?.ItemsFor(sessionID);

        if (items is null) return true;

        __result = respond(items);
        return false;
#endif
    }
}

/// <summary>
/// Makes SPT's own <c>InventoryHelper.GetPlayerStashSize</c> answer with a player's own
/// stash size, so items the server puts in their stash land in columns their game has.
/// </summary>
public static class StashSizePatch
{
    private static PlayerStashes? players;

    public static MethodBase Target()
        => AccessTools.Method(typeof(InventoryHelper), "GetPlayerStashSize", [typeof(PmcData)])
           ?? throw new MissingMethodException(nameof(InventoryHelper), "GetPlayerStashSize");

    public static void Use(PlayerStashes stashes) => players = stashes;

    public static void Postfix(PmcData pmcData, ref (int, int) __result)
    {
        if (players is null) return;

        try
        {
            __result = players.StashSize(pmcData, __result);
        }
        catch
        {
            // SPT's own answer stands: wrong for a player with a size of their own, but
            // the same answer every player got before 1.1.3.
        }
    }
}
