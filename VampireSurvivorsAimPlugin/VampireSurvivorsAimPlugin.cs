using System;
using System.Timers;
using HidWizards.UCR.Core.Attributes;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Utilities;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable MemberCanBePrivate.Global

namespace VampireSurvivorsAimPlugin;

[Plugin("Vampire Survivors Aim Plugin", Group = "Axis",
    Description = "Allow aiming using a second pair of axes when a button is held.")]
[PluginInput(DeviceBindingCategory.Range, "Movement X Axis")]
[PluginInput(DeviceBindingCategory.Range, "Movement Y Axis")]
[PluginInput(DeviceBindingCategory.Range, "Aim X Axis")]
[PluginInput(DeviceBindingCategory.Range, "Aim Y Axis")]
[PluginInput(DeviceBindingCategory.Momentary, "Hold for aiming mode")]
[PluginOutput(DeviceBindingCategory.Range, "X Axis")]
[PluginOutput(DeviceBindingCategory.Range, "Y Axis")]
public class VampireSurvivorsAimPlugin : Plugin
{
    [PluginGui("Aim Magnitude")] public int AimMagnitude { get; set; } = 100;

    [PluginGui("Aim Deadzone")] public int AimDeadzone { get; set; } = 50;

    [PluginGui("Active Aim Time (ms)")]
    public double ActiveAimTimeMs
    {
        get => _stopAim.Interval;
        set => _stopAim.Interval = value;
    }

    [PluginGui("Total Aim Directions")] public int NumAimDirections { get; set; } = 8;

    private State _state = State.Normal;
    private readonly Timer _stopAim = new();
    private bool _isAimPressed;
    private short[] _prevValues;
    private double _prevDiscretizedAimAngle;
    private readonly object _locker = new();

    public VampireSurvivorsAimPlugin()
    {
        _stopAim.Elapsed += OnTimerElapsed;
        _stopAim.AutoReset = false;
        ActiveAimTimeMs = 20;
    }

    public override void Update(params short[] values)
    {
        lock (_locker)
        {
            var moveX = values[0];
            var moveY = values[1];
            var aimX = values[2];
            var aimY = values[3];
            _isAimPressed = values[4] != 0;

            double aimMagnitude;
            {
                var a = (double)aimX;
                var b = (double)aimY;
                aimMagnitude = Math.Sqrt(a * a + b * b);
                if (aimMagnitude < (double)Constants.AxisMaxValue * AimDeadzone / 100)
                {
                    aimMagnitude = 0;
                }
            }

            var discretizedAimAngle = DiscretizeAimAngle(aimX, aimY);

            switch (_state)
            {
                case State.Normal:
                    if (_isAimPressed && aimMagnitude != 0)
                    {
                        _stopAim.Stop();
                        _stopAim.Start();

                        _state = State.ActiveAim;
                    }

                    break;
                case State.ActiveAim:
                    break;
                case State.PassiveAim:
                    if (!_isAimPressed)
                    {
                        _state = State.Normal;
                    }
                    // ReSharper disable once CompareOfFloatsByEqualityOperator
                    else if (aimMagnitude != 0 && discretizedAimAngle != _prevDiscretizedAimAngle)
                    {
                        _stopAim.Stop();
                        _stopAim.Start();

                        _state = State.ActiveAim;
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            switch (_state)
            {
                case State.Normal:
                    WriteOutput(0, moveX);
                    WriteOutput(1, moveY);

                    break;
                case State.ActiveAim:
                    var x = Math.Cos(discretizedAimAngle);
                    var y = Math.Sin(discretizedAimAngle);

                    var scaleFactor = Constants.AxisMaxValue * AimMagnitude / 100;

                    WriteOutput(0, (short)(x * scaleFactor));
                    WriteOutput(1, (short)(y * scaleFactor));

                    _prevDiscretizedAimAngle = discretizedAimAngle;

                    break;
                case State.PassiveAim:
                    WriteOutput(0, 0);
                    WriteOutput(1, 0);

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            _prevValues = values;
        }
    }

    private double DiscretizeAimAngle(double aimX, double aimY)
    {
        var theta = Math.Atan2(aimY, aimX);
        if (theta < 0)
        {
            theta += 2 * Math.PI;
        }

        var a = Math.Round(theta / (2 * Math.PI) * NumAimDirections);
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (a == NumAimDirections)
        {
            a = 0;
        }

        return a / NumAimDirections * 2 * Math.PI;
    }

    private void OnTimerElapsed(object sender, ElapsedEventArgs elapsedEventArgs)
    {
        _state = _isAimPressed ? State.PassiveAim : State.Normal;
        Update(_prevValues);
    }

    public override void OnActivate()
    {
        _state = State.Normal;
    }

    public override void OnDeactivate()
    {
        _stopAim?.Dispose();
    }
}

internal enum State
{
    Normal,
    ActiveAim,
    PassiveAim
}