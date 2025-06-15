namespace NetworkMonitor.Objects
{
    public class ResultObj
    {
        /// <summary>
        /// Result Obj that contains information on the result of the operation.
        /// </summary>
        public ResultObj()
        {
            this.message = "";
            this.success = false;
            this.data = null;
        }
        public ResultObj(string message, bool success)
        {
            this.message = message;
            this.success = success;
            this.data = null;
        }
        private string message="";
        private bool success;
        private object? data;

        /// <summary>
        /// A string contains a message from the result of the operation.
        /// </summary>
        public string Message { get => message; set => message = value; }

        /// <summary>
        /// A boolean that indicates if the operation was successful.
        /// </summary>
        public bool Success { get => success; set => success = value; }

        /// <summary>
        /// An object that contains data from the result of the operation. All times returned in the data will be UTC.
        /// </summary>
        public object? Data { get => data; set => data = value; }
    }
}
