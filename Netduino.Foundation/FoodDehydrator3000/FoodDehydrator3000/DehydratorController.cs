using System;
using System.Threading;
using Microsoft.SPOT;
using H = Microsoft.SPOT.Hardware;
using N = SecretLabs.NETMF.Hardware.Netduino;
using Netduino.Foundation.Sensors.Temperature;
using Netduino.Foundation.Relays;
using Netduino.Foundation.Sensors.Buttons;
using Netduino.Foundation.Generators;
using Netduino.Foundation.Displays;

namespace FoodDehydrator3000
{
    public class DehydratorController
    {
        // events
        public event EventHandler RunTimeElapsed = delegate { };

        // peripherals
        protected AnalogTemperature _tempSensor = null;
        protected SoftPwm _heaterRelayPwm = null;
        protected Relay _fanRelay = null;
        protected SerialLCD _display = null;

        // controllers
        PidController _pidController = null;

        // other members
        Thread _tempControlThread = null;
        int _powerUpdateInterval = 2000; // milliseconds; how often to update the power

        // properties
        public bool Running {
            get { return _running; }
        }
        protected bool _running = false;

        public float TargetTemperature { get; set; }

        public TimeSpan RunningTimeLeft
        {
            get { return _runningTimeLeft; }
        }
        protected TimeSpan _runningTimeLeft = TimeSpan.MinValue;

        public DehydratorController(AnalogTemperature tempSensor, SoftPwm heater, Relay fan, SerialLCD display)
        {
            _tempSensor = tempSensor;
            _heaterRelayPwm = heater;
            _fanRelay = fan;
            _display = display;

            _pidController = new PidController();
            _pidController.P = 0.005f; // proportional
            _pidController.I = 0.0001f; // integral
            _pidController.D = 0f; // derivative

        }


        public void TurnOff()
        {
            Debug.Print("Turning off.");
            this._fanRelay.IsOn = false;
            this._heaterRelayPwm.Stop();
            this._running = false;
            this._runningTimeLeft = TimeSpan.MinValue;
        }

        public void TurnOn(int temp)
        {
            TurnOn(temp, TimeSpan.MaxValue);
        }

        public void TurnOn(int temp, TimeSpan runningTime)
        {
            // set our state vars
            TargetTemperature = (float)temp;
            Debug.Print("Turning on.");
            this._runningTimeLeft = runningTime;
            this._running = true;

            Debug.Print("Here");

            // keeping fan off, to get temp to rise.
            this._fanRelay.IsOn = true;
            
            // TEMP - to be replaced with PID stuff
            this._heaterRelayPwm.Frequency = 1.0f / 5.0f; // 5 seconds to start (later we can slow down)
            // on start, if we're under temp, turn on the heat to start.
            float duty = (_tempSensor.Temperature < TargetTemperature) ? 1.0f : 0.0f;
            this._heaterRelayPwm.DutyCycle = duty;
            this._heaterRelayPwm.Start();

            // start our temp regulation thread. might want to change this to notify.
            StartRegulatingTemperatureThread();
        }

        protected void StartRegulatingTemperatureThread()
        {
            _tempControlThread = new Thread(() => {
                while (this._running) {
                    Debug.Print("Temp: " + _tempSensor.Temperature.ToString() + "�C");

                    // set our input and target on the PID calculator
                    _pidController.Input = _tempSensor.Temperature;
                    _pidController.TargetInput = this.TargetTemperature;

                    // get the appropriate power level
                    var powerLevel = _pidController.CalculatePowerOutput();
                    Debug.Print("Temp: " + _tempSensor.Temperature.ToString() + "�C");

                    // set our PWM appropriately
                    Debug.Print("Setting duty cycle to: " + (powerLevel / 1000).ToString("N0") + "%");
                    _display.WriteLine("Power: " + (powerLevel / 1000).ToString("N0") + "%", 0);
                    if (powerLevel > 1) powerLevel = 1;
                    if (powerLevel < 0) powerLevel = 0;
                    this._heaterRelayPwm.DutyCycle = powerLevel;

                    // sleep for a while. 
                    Thread.Sleep(_powerUpdateInterval);
                }
            });
            _tempControlThread.Start();
        }
    }
}
