using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace StS2AP.Utils
{
    public static class GodotUtility
    {
        /// <summary>
        /// Certain Godot Controls, such as Buttons, can have multiple signals we need to remove, but we lack the reference to the
        /// original callback. This method will disconnect all signal connections for a given signal on a target object.
        ///
        /// For example, I needed to use this to clear all signals on the cloned Archipelago Settings button, or else it's going to fire
        /// it's original callback when clicked as well.
        /// </summary>
        public static void DisconnectSignalConnections(GodotObject target, StringName signal)
        {
            foreach (var connection in target.GetSignalConnectionList(signal))
            {
                if (connection is Godot.Collections.Dictionary dict)
                {
                    var callable = (Callable)dict["callable"];
                    target.Disconnect(signal, callable);
                }
            }
        }
    }
}
