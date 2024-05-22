using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRageMath;
using static Scripts.ModularAssemblies.Communication.DefinitionDefs;

namespace Scripts.ModularAssemblies
{
    /* Hey there modders!
     *
     * This file is a *template*. Make sure to keep up-to-date with the latest version, which can be found at https://github.com/StarCoreSE/Modular-Assemblies-Client-Mod-Template.
     *
     * If you're just here for the API, head on over to https://github.com/StarCoreSE/Modular-Assemblies/wiki/The-Modular-API for a (semi) comprehensive guide.
     *
     */
    internal partial class ModularDefinition
    {
        internal ModularPhysicalDefinition OCFi_Definition => new ModularPhysicalDefinition
        {
            // Unique name of the definition.
            Name = "OCFi_Definition",

            OnInit = () =>
            {
                MyAPIGateway.Utilities.ShowMessage("Modular Assemblies", "OCFi_Definition.OnInit called.");
                EPRTerminalControls.CreateControlsOnce(); // Initialize terminal controls here
            },

            // Triggers whenever a new part is added to an assembly.
            OnPartAdd = (assemblyId, block, isBasePart) =>
            {
                MyAPIGateway.Utilities.ShowMessage("Modular Assemblies", $"OCFi_Definition.OnPartAdd called.\nAssembly: {assemblyId}\nBlock: {block.DisplayNameText}\nIsBasePart: {isBasePart}");
                MyAPIGateway.Utilities.ShowNotification("Assembly has " + ModularApi.GetMemberParts(assemblyId).Length + " blocks.");
            },

            // Triggers whenever a part is removed from an assembly.
            OnPartRemove = (assemblyId, block, isBasePart) =>
            {
                MyAPIGateway.Utilities.ShowMessage("Modular Assemblies", $"OCFi_Definition.OnPartRemove called.\nAssembly: {assemblyId}\nBlock: {block.DisplayNameText}\nIsBasePart: {isBasePart}");
                MyAPIGateway.Utilities.ShowNotification("Assembly has " + ModularApi.GetMemberParts(assemblyId).Length + " blocks.");
            },

            // Triggers whenever a part is destroyed, just after OnPartRemove.
            OnPartDestroy = (assemblyId, block, isBasePart) =>
            {
                // You can remove this function, and any others if need be.
                MyAPIGateway.Utilities.ShowMessage("Modular Assemblies", $"OCFi_Definition.OnPartDestroy called.\nI hope the explosion was pretty.");
                MyAPIGateway.Utilities.ShowNotification("Assembly has " + ModularApi.GetMemberParts(assemblyId).Length + " blocks.");
            },

            // Optional - if this is set, an assembly will not be created until a baseblock exists.
            BaseBlockSubtype = "OCFi_Reactor",

            // All SubtypeIds that can be part of this assembly.
            AllowedBlockSubtypes = new[]
            {
            "OCFi_Conduit",
            "OCFi_Nozzle",
            "OCFi_Reactor",
        },

            // Allowed connection directions & whitelists, measured in blocks.
            // If an allowed SubtypeId is not included here, connections are allowed on all sides.
            // If the connection type whitelist is empty, all allowed subtypes may connect on that side.
            AllowedConnections = new Dictionary<string, Dictionary<Vector3I, string[]>>
            {
                ["OCFi_Reactor"] = new Dictionary<Vector3I, string[]>
                {
                    [Vector3I.Forward] = Array.Empty<string>(),
                    [Vector3I.Backward] = Array.Empty<string>(),
                    [Vector3I.Left] = Array.Empty<string>(),
                    [Vector3I.Right] = Array.Empty<string>(),
                    [Vector3I.Up] = Array.Empty<string>(),
                    [Vector3I.Down] = Array.Empty<string>(),
                },
                ["OCFi_Nozzle"] = new Dictionary<Vector3I, string[]>
                {
                    [Vector3I.Forward] = Array.Empty<string>(),
                },
                ["OCFi_Conduit"] = new Dictionary<Vector3I, string[]>
                {
                    [Vector3I.Forward] = Array.Empty<string>(),
                    [Vector3I.Backward] = Array.Empty<string>(),
                }
            },
        };
    }
}
