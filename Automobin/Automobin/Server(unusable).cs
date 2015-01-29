using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Net;
using System.Net.Sockets;

namespace TCPServer
{
	class Server
	{
		private TcpListener tcpListener;
		private Thread listenThread;
		private string messageToSend;
		private bool messageToSendFlag = false;
		private string messageReceived;
		private bool messageReceivedFlag = false;

		public Server()
		{
			this.tcpListener = new TcpListener(IPAddress.Any, 8234);
			this.listenThread = new Thread(new ThreadStart(ListenForClients));
			this.listenThread.Start();
		}

		public void setMessage(string message)
		{
			this.messageToSend = message;
			messageToSendFlag = true;
		}

		public string getMessage()
		{
			if (messageReceivedFlag)
				return messageReceived;
			else
				return null;
		}

		private void ListenForClients()
		{
			this.tcpListener.Start();
			while(true)
			{
				TcpClient client = this.tcpListener.AcceptTcpClient();
				Thread clientThread = new Thread(new ParameterizedThreadStart(HandleClientComm));
				clientThread.Start(client);
			}
		}

		private void HandleClientComm(object client)
		{
			TcpClient tcpClient = (TcpClient)client;
			NetworkStream clientStream = tcpClient.GetStream();

			byte[] message = new byte[4096];
			int bytesRead;

			while(true)
			{
				bytesRead = 0;
				try
				{
					bytesRead = clientStream.Read(message, 0, 4096);
				}
				catch
				{
					break;
				}
				if(bytesRead == 0)
				{
					break;
				}
				ASCIIEncoding encoder = new ASCIIEncoding();
				messageReceived = encoder.GetString(message, 0, bytesRead);
				messageReceivedFlag = true;

				if(messageToSendFlag)
				{
					byte[] buffer = encoder.GetBytes(messageToSend);
					clientStream.Write(buffer, 0, buffer.Length);
					clientStream.Flush();
					messageToSendFlag = false;
				}
			}
			tcpClient.Close();
		}

	}
}
