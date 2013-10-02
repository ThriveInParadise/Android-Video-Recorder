using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices.Automation;

namespace AVU
{
    class ExecuteCmd
    {
        public void ExecuteCommandSync(object command)
        {
            try
            {
                using (dynamic shell = AutomationFactory.CreateObject("WScript.Shell"))
                {
                    shell.Run((string) command);
                }
            }
            catch (Exception objException)
            {
            }
        }

        public void ExecuteCommandAsync(string command)
        {
            try
            {
                //Asynchronously start the Thread to process the Execute command request.
                Thread objThread = new Thread(new ParameterizedThreadStart(ExecuteCommandSync));

                //Make the thread as background thread.
                objThread.IsBackground = true;

                //Start the thread.
                objThread.Start(command);
            }
            catch (Exception objException)
            {
                // Log the exception
            }
        }
    }
}
