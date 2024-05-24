using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRageMath;
using static Scripts.ModularAssemblies.Communication.DefinitionDefs;

namespace Scripts.ModularAssemblies
{
    internal partial class ModularDefinition
    {
        internal ModularPhysicalDefinition OCFi_Definition => new ModularPhysicalDefinition
        {
            Name = "OCFi_Definition",

            OnInit = () =>
            {
                MyAPIGateway.Utilities.ShowMessage("Modular Assemblies", "OCFi_Definition.OnInit called.");
                EPRTerminalControls.CreateControlsOnce(); // Initialize terminal controls here
            },

            OnPartAdd = (assemblyId, block, isBasePart) =>
            {
                OCFiManager.I.OnPartAdd(assemblyId, block, isBasePart);
                MyAPIGateway.Utilities.ShowMessage("Modular Assemblies", $"OCFi_Definition.OnPartAdd called.\nAssembly: {assemblyId}\nBlock: {block.DisplayNameText}\nIsBasePart: {isBasePart}");
            },

            OnPartRemove = (assemblyId, block, isBasePart) =>
            {
                OCFiManager.I.OnPartRemove(assemblyId, block, isBasePart);
                MyAPIGateway.Utilities.ShowMessage("Modular Assemblies", $"OCFi_Definition.OnPartRemove called.\nAssembly: {assemblyId}\nBlock: {block.DisplayNameText}\nIsBasePart: {isBasePart}");
            },

            OnPartDestroy = (assemblyId, block, isBasePart) =>
            {
                MyAPIGateway.Utilities.ShowMessage("Modular Assemblies", $"OCFi_Definition.OnPartDestroy called.\nI hope the explosion was pretty.");
            },

            BaseBlockSubtype = "OCFi_Reactor",

            AllowedBlockSubtypes = new[]
            {
                "OCFi_Conduit",
                "OCFi_Nozzle",
                "OCFi_Reactor",
            },

            AllowedConnections = new Dictionary<string, Dictionary<Vector3I, string[]>>
            {
                ["OCFi_Reactor"] = new Dictionary<Vector3I, string[]>
                {
                    [Vector3I.Forward * 2] = Array.Empty<string>(),
                    [Vector3I.Backward * 2] = Array.Empty<string>(),
                    [Vector3I.Left * 2] = Array.Empty<string>(),
                    [Vector3I.Right * 2] = Array.Empty<string>(),
                    [Vector3I.Up * 2] = Array.Empty<string>(),
                    [Vector3I.Down * 2] = Array.Empty<string>(),
                },
                ["OCFi_Nozzle"] = new Dictionary<Vector3I, string[]>
                {
                    [Vector3I.Backward] = Array.Empty<string>(),
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
