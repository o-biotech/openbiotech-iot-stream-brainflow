using brainflow;
using Microsoft.Azure.Devices.Client;
using System.IO.Ports;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace openbiotech_iot_stream_brainflow
{
    public interface IBackgroundTaskQueue
    {
        ValueTask QueueBackgroundWorkItemAsync(
            Func<CancellationToken, ValueTask> workItem);

        ValueTask<Func<CancellationToken, ValueTask>> DequeueAsync(
            CancellationToken cancellationToken);
    }

    public sealed class DefaultBackgroundTaskQueue : IBackgroundTaskQueue
    {
        private readonly Channel<Func<CancellationToken, ValueTask>> _queue;

        public DefaultBackgroundTaskQueue(int capacity)
        {
            BoundedChannelOptions options = new(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait
            };
            _queue = Channel.CreateBounded<Func<CancellationToken, ValueTask>>(options);
        }

        public async ValueTask QueueBackgroundWorkItemAsync(
            Func<CancellationToken, ValueTask> workItem)
        {
            if (workItem is null)
            {
                throw new ArgumentNullException(nameof(workItem));
            }

            await _queue.Writer.WriteAsync(workItem);
        }

        public async ValueTask<Func<CancellationToken, ValueTask>> DequeueAsync(
            CancellationToken cancellationToken)
        {
            Func<CancellationToken, ValueTask>? workItem =
                await _queue.Reader.ReadAsync(cancellationToken);

            return workItem;
        }
    }

    public static class Globals
    {
        public static double[,] UNPROCESSED_DATA;
    }

    public sealed class MonitorLoop
    {
        private readonly IBackgroundTaskQueue _taskQueue;
        private readonly ILogger<MonitorLoop> _logger;
        private readonly CancellationToken _cancellationToken;
        public int _timestamp;
        public DeviceClient _deviceClient;
        public List<int> _channels = new List<int>();
        public string _connectionString;
        public string _selectedPort;

        public MonitorLoop(
            IBackgroundTaskQueue taskQueue,
            ILogger<MonitorLoop> logger,
            IHostApplicationLifetime applicationLifetime)
        {
            _taskQueue = taskQueue;
            _logger = logger;
            _cancellationToken = applicationLifetime.ApplicationStopping;
        }

        public int parse_args(string[] args, BrainFlowInputParams input_params)
        {
            int board_id = (int)BoardIds.EMOTIBIT_BOARD;

            Console.WriteLine("OpenBiotech IoT Stream - Brainflow");
            Console.WriteLine();
            Console.WriteLine("Ensure that your Cyton board is connected to your computer, and that you've created a device in Fathym's IoT Ensemble");
            Console.WriteLine("Create a device here: https://www.fathym.com/dashboard/iot");
            Console.WriteLine();
            Console.WriteLine("Please enter your Fathym IoT Ensemble device connection string:");
            _connectionString = Console.ReadLine();

            string[] ports = SerialPort.GetPortNames();

            try
            {
                if (ports.Length > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Available Ports:");
                    for (int i = 0; i < ports.Length; i++)
                    {
                        Console.WriteLine($"{i + 1}. {ports[i]}");
                    }

                    Console.WriteLine();
                    Console.Write("Select the port of your Cyton (enter the number): ");
                    if (int.TryParse(Console.ReadLine(), out int choice) && choice >= 1 && choice <= ports.Length)
                    {
                        _selectedPort = ports[choice - 1];
                        // Now you can use the selectedPort variable for further actions.
                    }
                    else
                    {
                        Console.WriteLine("Invalid choice. Please select a valid number from the list.");
                    }
                }
                else
                {
                    throw new Exception("No COM ports found on this machine.");
                }

                //input_params.serial_port = _selectedPort;
                //assume synthetic board by default
                // use docs to get params for your specific board, e.g. set serial_port for Cyton
                //for (int i = 0; i < args.Length; i++)
                //{
                //    if (args[i].Equals("--ip-address"))
                //    {
                //        input_params.ip_address = args[i + 1];
                //    }
                //    if (args[i].Equals("--mac-address"))
                //    {
                //        input_params.mac_address = args[i + 1];
                //    }
                //    if (args[i].Equals("--serial-port"))
                //    {
                //        input_params.serial_port = args[i + 1];
                //    }
                //    if (args[i].Equals("--other-info"))
                //    {
                //        input_params.other_info = args[i + 1];
                //    }
                //    if (args[i].Equals("--ip-port"))
                //    {
                //        input_params.ip_port = Convert.ToInt32(args[i + 1]);
                //    }
                //    if (args[i].Equals("--ip-protocol"))
                //    {
                //        input_params.ip_protocol = Convert.ToInt32(args[i + 1]);
                //    }
                //    if (args[i].Equals("--board-id"))
                //    {
                //        board_id = Convert.ToInt32(args[i + 1]);
                //    }
                //    if (args[i].Equals("--timeout"))
                //    {
                //        input_params.timeout = Convert.ToInt32(args[i + 1]);
                //    }
                //    if (args[i].Equals("--serial-number"))
                //    {
                //        input_params.serial_number = args[i + 1];
                //    }
                //    if (args[i].Equals("--file"))
                //    {
                //        input_params.file = args[i + 1];
                //    }
                //}
                return board_id;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());

                return board_id;
            }
        }

        public void StartMonitorLoop(string[] args)
        {
            _logger.LogInformation($"{nameof(MonitorAsync)} loop is starting.");

            Task.Run(async () => await MonitorAsync(args));
        }

        public async ValueTask MonitorAsync(string[] args)
        {
            try
            {
                BrainFlowInputParams input_params = new BrainFlowInputParams();

                int board_id = parse_args(args, input_params);

                BoardShim board_shim = new BoardShim(board_id, input_params);

                BoardDescr something = BoardShim.get_board_descr<BoardDescr>(board_id, 2);

                Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(something));
                

                board_shim.prepare_session();

                //Console.WriteLine(BoardShim.get_board_descr(board_id));


                int[] magnetometerChannels = BoardShim.get_magnetometer_channels(board_id, 0);

                int[] accelChannels = BoardShim.get_accel_channels(board_id, 0);

                int[] gyroChannels = BoardShim.get_gyro_channels(board_id, 0);

                int[] ppgChannels = BoardShim.get_ppg_channels(board_id, 1);

                int[] temperatureChannels = BoardShim.get_temperature_channels(board_id, 2);

                int[] edaChannels = BoardShim.get_eda_channels(board_id, 2);

                foreach(int channel in magnetometerChannels)
                {
                    _channels.Add(channel);
                }

                foreach (int channel in accelChannels)
                {
                    _channels.Add(channel);
                }

                foreach (int channel in gyroChannels)
                {
                    _channels.Add(channel);
                }

                foreach (int channel in ppgChannels)
                {
                    _channels.Add(channel);
                }

                foreach (int channel in temperatureChannels)
                {
                    _channels.Add(channel);
                }

                foreach (int channel in edaChannels)
                {
                    _channels.Add(channel);
                }

                //_channels.Add(
                //    foreach (int channel in magnetometerChannels) )

                _timestamp = BoardShim.get_timestamp_channel(board_id);

                _deviceClient = DeviceClient.CreateFromConnectionString(_connectionString);

                board_shim.start_stream();

                int count = 0;

                while (true)
                {
                    System.Threading.Thread.Sleep(5000);

                    Globals.UNPROCESSED_DATA = board_shim.get_board_data();
                    count++;
                    Console.WriteLine();
                    Console.WriteLine($"Data Received, loop number {count}");
                    Console.WriteLine($"Start Time:{Globals.UNPROCESSED_DATA[_timestamp, 0]}");
                    Console.WriteLine($"End Time:{Globals.UNPROCESSED_DATA[_timestamp, Globals.UNPROCESSED_DATA.GetLength(1) - 1]}");

                    await _taskQueue.QueueBackgroundWorkItemAsync(BuildWorkItemAsync);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

        }

        private async ValueTask BuildWorkItemAsync(CancellationToken token)
        {
            try
            {
                double[,] working_data = Globals.UNPROCESSED_DATA;

                Globals.UNPROCESSED_DATA = new double[24, 5000];

                for (int col = 1; col <= working_data.GetLength(1); col += 200)
                {
                    Dictionary<string, object> message = new Dictionary<string, object>();

                    DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds((long)(working_data[_timestamp, col] * 1000));

                    message["timestamp"] = dateTimeOffset.DateTime;

                    message["DeviceID"] = "CytonBoard";

                    message["DeviceType"] = "EEG";

                    message["Version"] = "1.0";

                    var sensorReadings = new Dictionary<string, object>();

                    foreach (int channel in _channels)
                    {
                        sensorReadings[channel.ToString()] = working_data[channel, col].ToString();
                    }

                    message["SensorReadings"] = sensorReadings;

                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(message);

                    var mesg = new Message(Encoding.UTF8.GetBytes(json));

                    await _deviceClient.SendEventAsync(mesg);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }
    }

    public sealed class QueuedHostedService : BackgroundService
    {
        private readonly IBackgroundTaskQueue _taskQueue;
        private readonly ILogger<QueuedHostedService> _logger;

        public QueuedHostedService(
            IBackgroundTaskQueue taskQueue,
            ILogger<QueuedHostedService> logger) =>
            (_taskQueue, _logger) = (taskQueue, logger);

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                $"{nameof(QueuedHostedService)} is running.{Environment.NewLine}");

            return ProcessTaskQueueAsync(stoppingToken);
        }

        private async Task ProcessTaskQueueAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    Func<CancellationToken, ValueTask>? workItem =
                        await _taskQueue.DequeueAsync(stoppingToken);

                    await workItem(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Prevent throwing if stoppingToken was signaled
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred executing task work item.");
                }
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                $"{nameof(QueuedHostedService)} is stopping.");

            await base.StopAsync(stoppingToken);
        }
    }
}