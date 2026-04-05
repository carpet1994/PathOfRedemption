// =============================================================================
//  La Via della Redenzione — Systems/InputSystem.cs
//  Package : com.refa.valdrath
//
//  CORREZIONE BUG C1:
//    Il file originale conteneva sia l'enum InputAction (namespace Core) che
//    la classe InputSystem (namespace Systems) con i `using` scritti DOPO la
//    chiusura del primo namespace — sintassi illegale in C#.
//    Ora InputAction è in Core/InputActions.cs (file separato).
//    Questo file contiene solo InputSystem e InputVector2, con tutti i `using`
//    correttamente posizionati in cima.
// =============================================================================

using System;
using System.Collections.Generic;
using LaViaDellaRedenzione.Core;

namespace LaViaDellaRedenzione.Systems
{
    /// <summary>
    /// Vettore 2D semplice per l'asse analogico di navigazione.
    /// X: -1.0 (sinistra) → +1.0 (destra)
    /// Y: -1.0 (su)        → +1.0 (giù)
    /// </summary>
    public readonly struct InputVector2
    {
        public float X { get; }
        public float Y { get; }

        public InputVector2(float x, float y) { X = x; Y = y; }

        public static readonly InputVector2 Zero = new(0f, 0f);

        public override string ToString() => $"({X:F2}, {Y:F2})";
    }

    /// <summary>
    /// Singleton. Punto unico di accesso allo stato di input per frame.
    /// </summary>
    public sealed class InputSystem
    {
        private static InputSystem? _instance;
        public static InputSystem Instance => _instance ??= new InputSystem();
        private InputSystem() { }

        private readonly HashSet<InputAction> _current  = new();
        private readonly HashSet<InputAction> _previous = new();
        private InputVector2 _navigationAxis = InputVector2.Zero;

        public InputDevice CurrentDevice { get; private set; } = InputDevice.Touch;

        public event Action<InputDevice>? OnDeviceChanged;

        public void SetPressed(InputAction action, bool pressed, InputDevice device)
        {
            if (device != CurrentDevice)
            {
                CurrentDevice = device;
                OnDeviceChanged?.Invoke(device);
            }

            if (pressed) _current.Add(action);
            else         _current.Remove(action);
        }

        public void SetNavigationAxis(float x, float y, InputDevice device)
        {
            _navigationAxis = new InputVector2(x, y);

            if (device != CurrentDevice)
            {
                CurrentDevice = device;
                OnDeviceChanged?.Invoke(device);
            }

            const float threshold = 0.5f;
            SetPressedInternal(InputAction.NavigateLeft,  x < -threshold);
            SetPressedInternal(InputAction.NavigateRight, x >  threshold);
            SetPressedInternal(InputAction.NavigateUp,    y < -threshold);
            SetPressedInternal(InputAction.NavigateDown,  y >  threshold);
        }

        /// <summary>
        /// Aggiorna lo stato precedente. Chiamare ALL'INIZIO di ogni frame.
        /// </summary>
        public void Update()
        {
            _previous.Clear();
            foreach (var action in _current)
                _previous.Add(action);
        }

        public bool IsPressed(InputAction action)
        {
            if (action == InputAction.AnyInput) return _current.Count > 0;
            return _current.Contains(action);
        }

        public bool IsJustPressed(InputAction action)
        {
            if (action == InputAction.AnyInput) return HasAnyJustPressed();
            return _current.Contains(action) && !_previous.Contains(action);
        }

        public bool IsJustReleased(InputAction action)
        {
            if (action == InputAction.AnyInput) return HasAnyJustReleased();
            return !_current.Contains(action) && _previous.Contains(action);
        }

        public InputVector2 GetNavigationAxis() => _navigationAxis;

        public bool IsNavigating()
            => IsPressed(InputAction.NavigateLeft)
            || IsPressed(InputAction.NavigateRight)
            || IsPressed(InputAction.NavigateUp)
            || IsPressed(InputAction.NavigateDown);

        private void SetPressedInternal(InputAction action, bool pressed)
        {
            if (pressed) _current.Add(action);
            else         _current.Remove(action);
        }

        private bool HasAnyJustPressed()
        {
            foreach (var action in _current)
                if (!_previous.Contains(action)) return true;
            return false;
        }

        private bool HasAnyJustReleased()
        {
            foreach (var action in _previous)
                if (!_current.Contains(action)) return true;
            return false;
        }

        /// <summary>
        /// Svuota lo stato. Da chiamare ad ogni transizione di schermata.
        /// </summary>
        public void Flush()
        {
            _current.Clear();
            _previous.Clear();
            _navigationAxis = InputVector2.Zero;
        }
    }
}
