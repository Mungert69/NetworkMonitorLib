using NetworkMonitor.Objects;
using NetworkMonitor.Utils.Helpers;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using NetworkMonitor.Objects.ServiceMessage;
using System.Collections.Generic;
using System;
using System.Text;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using NetworkMonitor.Utils;
using NetworkMonitor.Objects.Repository;

namespace NetworkMonitor.Objects.Repository
{
    public interface IProcessorStateRabbitListner
    {
        Task<ResultObj> AddProcessor(ProcessorObj? processorObj);
        Task<ResultObj> UpdateProcessor(ProcessorObj? processorObj);
        Task<ResultObj> FullProcessorList(List<ProcessorObj>? processorObjs);
        Task Shutdown();
        Task<ResultObj> Setup();

    }

    public class ProcessorStateRabbitListner : RabbitListenerBase, IProcessorStateRabbitListner
    {
        private IProcessorState _processorState = new ProcessorState();
        private IFileRepo _fileRepo;
        private readonly SemaphoreSlim _processorLock = new SemaphoreSlim(1, 1);



        public ProcessorStateRabbitListner(ILogger<RabbitListenerBase> logger, ISystemParamsHelper systemParamsHelper, IProcessorState processorState, IFileRepo fileRepo)
            : base(logger, DeriveSystemUrl(systemParamsHelper))
        {

            _processorState = processorState;
            _fileRepo = fileRepo;
            _ = Setup();
        }
        private static SystemUrl DeriveSystemUrl(ISystemParamsHelper systemParamsHelper)
        {
            return systemParamsHelper.GetSystemParams().ThisSystemUrl;
        }


        protected override void InitRabbitMQObjs()
        {
            _rabbitMQObjs.Add(new RabbitMQObj()
            {
                ExchangeName = "addProcessor",
                FuncName = "addProcessor",
                MessageTimeout = 2160000
            });


            _rabbitMQObjs.Add(new RabbitMQObj()
            {
                ExchangeName = "updateProcessor",
                FuncName = "updateProcessor",
                MessageTimeout = 2160000
            });
            _rabbitMQObjs.Add(new RabbitMQObj()
            {
                ExchangeName = "fullProcessorList",
                FuncName = "fullProcessorList",
                MessageTimeout = 2160000
            });

        }
        protected override async Task<ResultObj> DeclareConsumers()
        {
            var result = new ResultObj();
            try
            {
                foreach (var rabbitMQObj in _rabbitMQObjs)
                {
                    if (rabbitMQObj.ConnectChannel != null)
                    {

                        rabbitMQObj.Consumer = new AsyncEventingBasicConsumer(rabbitMQObj.ConnectChannel);
                        await rabbitMQObj.ConnectChannel.BasicConsumeAsync(
                                queue: rabbitMQObj.QueueName,
                                autoAck: false,
                                consumer: rabbitMQObj.Consumer
                            );
                        switch (rabbitMQObj.FuncName)
                        {
                            case "addProcessor":
                                await rabbitMQObj.ConnectChannel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false);
                                rabbitMQObj.Consumer.ReceivedAsync += async (model, ea) =>
                            {
                                try
                                {
                                    var tResult = await AddProcessor(ConvertToObject<ProcessorObj>(model, ea));
                                    result.Success = tResult.Success;
                                    result.Message = tResult.Message;
                                    result.Data = tResult.Data;
                                    await rabbitMQObj.ConnectChannel.BasicAckAsync(ea.DeliveryTag, false);

                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(" Error : RabbitListener.DeclareConsumers.addProcessor " + ex.Message);
                                }
                            };
                                break;
                            case "updateProcessor":
                                await rabbitMQObj.ConnectChannel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false);
                                rabbitMQObj.Consumer.ReceivedAsync += async (model, ea) =>
                            {
                                try
                                {
                                    var tResult = await UpdateProcessor(ConvertToObject<ProcessorObj>(model, ea));
                                    result.Success = tResult.Success;
                                    result.Message = tResult.Message;
                                    result.Data = tResult.Data;
                                    await rabbitMQObj.ConnectChannel.BasicAckAsync(ea.DeliveryTag, false);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(" Error : RabbitListener.DeclareConsumers.addProcessor " + ex.Message);
                                }
                            };
                                break;
                            case "fullProcessorList":
                                await rabbitMQObj.ConnectChannel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false);
                                rabbitMQObj.Consumer.ReceivedAsync += async (model, ea) =>
                            {
                                try
                                {
                                    var tResult = await FullProcessorList(ConvertToList<List<ProcessorObj>>(model, ea));
                                    result.Success = tResult.Success;
                                    result.Message = tResult.Message;
                                    result.Data = tResult.Data;
                                    await rabbitMQObj.ConnectChannel.BasicAckAsync(ea.DeliveryTag, false);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(" Error : RabbitListener.DeclareConsumers.addProcessor " + ex.Message);
                                }
                            };
                                break;

                        }

                    }
                }
                result.Success = true;
                result.Message += " Success : Declared all consumers ";
            }
            catch (Exception e)
            {
                string message = " Error : failed to declate consumers. Error was : " + e.ToString() + " . ";
                result.Message += message;
                _logger.LogError(result.Message);
                result.Success = false;
            }
            return result;
        }

        public async Task<ResultObj> AddProcessor(ProcessorObj? processorObj)
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : AddProcessor : ";
            await _processorLock.WaitAsync();
            try
            {
                if (processorObj == null)
                {
                    result.Message += " Error : processorObj  is Null ";
                    return result;
                }
                try
                {
                    if (!_processorState.IsProcessorWithID(processorObj.AppID))
                    {
                        _processorState.ConcurrentProcessorList.Add(processorObj);
                        var resultStateChange = _processorState.AddAppIDStateChange(processorObj.AppID);
                        if (resultStateChange.Success) result.Message += $" {resultStateChange.Message} : Success : Added ProcessorObj {processorObj.AppID}";
                        result.Success = resultStateChange.Success;
                    }
                    else
                    {
                        result.Success = false;
                        result.Message += $" Error : Processor {processorObj.AppID} already exists ";
                        _logger.LogError(result.Message);
                        return result;
                    }

                }
                catch (Exception e)
                {
                    result.Data = null;
                    result.Success = false;
                    result.Message += "Error : Failed to receive message : Error was : " + e.Message + " ";
                    _logger.LogError(result.Message);
                    return result;
                }

                try
                {
                    await _fileRepo.SaveStateJsonAsync<List<ProcessorObj>>("ProcessorList", _processorState.GetProcessorListAll(true));
                    result.Message += " Success : Saved ProcessorList to State .";
                }
                catch (Exception e)
                {
                    result.Data = null;
                    result.Success = false;
                    result.Message += "Error : Failed to save Processor List to State : Error was : " + e.Message + " ";
                    _logger.LogError(result.Message);
                }


                _logger.LogInformation(result.Message);
            }
            finally
            {
                _processorLock.Release();
            }
            return result;
        }

        public async Task<ResultObj> UpdateProcessor(ProcessorObj? processorObj)
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : UpdateProcessor : ";
            await _processorLock.WaitAsync();
            try
            {
                if (processorObj == null)
                {
                    result.Message += " Error : processorObj  is Null ";
                    return result;
                }
                try
                {
                    var updateProcessor = _processorState.ConcurrentProcessorList.Where(w => w.AppID == processorObj.AppID).FirstOrDefault();
                    if (updateProcessor != null)
                    {
                        updateProcessor.DisabledEndPointTypes = processorObj.DisabledEndPointTypes;
                        updateProcessor.Location = processorObj.Location;
                        updateProcessor.MaxLoad = processorObj.MaxLoad;
                        updateProcessor.IsEnabled = processorObj.IsEnabled;
                        updateProcessor.AuthKey = processorObj.AuthKey;
                        updateProcessor.CustomConnects = processorObj.CustomConnects;
                        var resultStateChange = _processorState.AddAppIDStateChange(processorObj.AppID);
                        if (resultStateChange.Success) result.Message += $" {resultStateChange.Message} : Success : Updated ProcessorObj {processorObj.AppID}";
                        result.Success = resultStateChange.Success;
                    }
                    else
                    {
                        result.Success = false;
                        result.Message += $" Error : Processor {processorObj.AppID} does not exsist ";
                        _logger.LogError(result.Message);
                        return result;
                    }

                }
                catch (Exception e)
                {
                    result.Data = null;
                    result.Success = false;
                    result.Message += "Error : Failed to receive message : Error was : " + e.Message + " ";
                    _logger.LogError(result.Message);
                    return result;
                }
                try
                {
                    await _fileRepo.SaveStateJsonAsync<List<ProcessorObj>>("ProcessorList", _processorState.GetProcessorListAll(true));
                    result.Message += " Success : Saved ProcessorList to State .";
                }
                catch (Exception e)
                {
                    result.Data = null;
                    result.Success = false;
                    result.Message += "Error : Failed to save Processor List to State : Error was : " + e.Message + " ";
                    _logger.LogError(result.Message);
                    return result;
                }

                _logger.LogInformation(result.Message);
            }
            finally
            {
                _processorLock.Release();
            }
            return result;
        }

        /*  public ResultObj FullProcessorList(List<ProcessorObj>? processorObjs)
          {
              ResultObj result = new ResultObj();
              result.Success = false;
              result.Message = "MessageAPI : FullProcessorList : ";
              if (processorObjs == null)
              {
                  result.Message += " Error : processorObjs  is Null ";
                  return result;
              }
              try
              {
                  _processorState.ConcurrentProcessorList = processorObjs;
                  result.Message += " Success : Updated Full Processor List to ProcessorState .";

              }
              catch (Exception e)
              {
                  result.Data = null;
                  result.Success = false;
                  result.Message += "Error : Failed to receive message : Error was : " + e.Message + " ";
                  _logger.LogError(result.Message);
                  return result;
              }
              try
              {
                  _fileRepo.SaveStateJsonAsync<List<ProcessorObj>>("ProcessorList", _processorState.ProcessorList.ToList());
                  result.Message += $" Success : Saved {_processorState.ProcessorList.Count} Processors to State .";
              }
              catch (Exception e)
              {
                  result.Data = null;
                  result.Success = false;
                  result.Message += "Error : Failed to save Processor List to State : Error was : " + e.Message + " ";
                  _logger.LogError(result.Message);
                  return result;
              }
              result.Success = true;
              _logger.LogInformation(result.Message);
              return result;
          }*/

        public async Task<ResultObj> FullProcessorList(List<ProcessorObj>? processorObjs)
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : FullProcessorList : ";
            await _processorLock.WaitAsync();
            try
            {
                if (processorObjs == null)
                {
                    result.Message += " Error : processorObjs  is Null ";
                    return result;
                }
                try
                {
                    // Clear existing processors before updating (optional)
                    _processorState.ResetConcurrentProcessorList(processorObjs);

                    result.Message += " Success : Updated Full Processor List to ProcessorState .";

                }
                catch (Exception e)
                {
                    result.Data = null;
                    result.Success = false;
                    result.Message += "Error : Failed to receive message : Error was : " + e.Message + " ";
                    _logger.LogError(result.Message);
                    return result;
                }
                try
                {
                    await _fileRepo.SaveStateJsonAsync<List<ProcessorObj>>("ProcessorList", _processorState.GetProcessorListAll(true));
                    result.Message += $" Success : Saved {_processorState.GetProcessorListAll(true).Count} Processors to State .";
                }
                catch (Exception e)
                {
                    result.Data = null;
                    result.Success = false;
                    result.Message += "Error : Failed to save Processor List to State : Error was : " + e.Message + " ";
                    _logger.LogError(result.Message);
                    return result;
                }
                result.Success = true;
                _logger.LogInformation(result.Message);
            }
            finally
            {
                _processorLock.Release();
            }
            return result;
        }


    }
}
