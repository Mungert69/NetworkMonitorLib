using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using NetworkMonitor.Objects;

namespace NetworkMonitorService.Objects.ServiceMessage
{
    public class TaskQueue
{
    private SemaphoreSlim semaphore;
        private List<Task> taskList = new List<Task>();
    private bool isAcceptingTasks = true;
    public TaskQueue()
    {
        semaphore = new SemaphoreSlim(1);
    }

    public async Task<T> EnqueueBytes<T>(Func<byte[],Task<T>> taskGenerator, byte[] data) 
    {
        if (!isAcceptingTasks)
            throw new InvalidOperationException("Queue is not accepting new tasks");

        await semaphore.WaitAsync();
        try
        {
            var task = taskGenerator(data);
            taskList.Add(task);
            return await task;
        }
        finally
        {
            semaphore.Release();
        }
        
    }
     public async Task<T> EnqueueString<T>(Func<string,Task<T>> taskGenerator, string data) 
    {
        if (!isAcceptingTasks)
            throw new InvalidOperationException("Queue is not accepting new tasks");

        await semaphore.WaitAsync();
        try
        {
            var task = taskGenerator(data);
            taskList.Add(task);
            return await task;
        }
        finally
        {
            semaphore.Release();
        }
        
    }

    public async Task<T> EnqueueTuple<T>(Func<Tuple<string,string>,Task<T>> taskGenerator, Tuple<string,string> data) 
    {
        if (!isAcceptingTasks)
            throw new InvalidOperationException("Queue is not accepting new tasks");

        await semaphore.WaitAsync();
        try
        {
            var task =  taskGenerator(data);
            taskList.Add(task);
            return await task;
        }
        finally
        {
            semaphore.Release();
        }
      
    }

     public async Task<T> EnqueueStatusString<T>(Func<string,List<IAlertable>,Task<T>> taskGenerator, string data, List<IAlertable> monitorStatusAlerts) 
    {
        if (!isAcceptingTasks)
            throw new InvalidOperationException("Queue is not accepting new tasks");

        await semaphore.WaitAsync();
        try
        {
            var task = taskGenerator(data, monitorStatusAlerts);
            taskList.Add(task);
            return await task;
        }
        finally
        {
            semaphore.Release();
        }
     
    }
     public async Task<T> EnqueuePredictString<T>(Func<string,List<PredictStatusAlert>,Task<T>> taskGenerator, string data, List<PredictStatusAlert> predictStatusAlerts) 
    {
        if (!isAcceptingTasks)
            throw new InvalidOperationException("Queue is not accepting new tasks");

        await semaphore.WaitAsync();
        try
        {
            var task = taskGenerator(data, predictStatusAlerts);
            taskList.Add(task);
            return await task;
        }
        finally
        {
            semaphore.Release();
        }
     
    }
    
     public async Task<T> Enqueue<T>(Func<Task<T>> taskGenerator)
    {
        if (!isAcceptingTasks)
            throw new InvalidOperationException("Queue is not accepting new tasks");

        await semaphore.WaitAsync();
        try
        {
            var task = taskGenerator();
            taskList.Add(task);
            return await task;
        }
        finally
        {
            semaphore.Release();
        }
      
    }
    public async Task Enqueue(Func<Task> taskGenerator)
    {
        if (!isAcceptingTasks)
            throw new InvalidOperationException("Queue is not accepting new tasks");

        await semaphore.WaitAsync();
        try
        {
            var task = taskGenerator();
            taskList.Add(task);
            await task;
        }
        finally
        {
            semaphore.Release();
        }
       
    }
    public void StopAcceptingTasks()
    {
        isAcceptingTasks = false;
    }

    public async Task WaitForAllTasksToComplete()
    {
        await Task.WhenAll(taskList);
    }
}

}