using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Automobin
{
	class Server
	{
		private IPAddress ipAddress;
		private TcpListener tcpListener;
		private Thread listenThread;
		private Thread socketThread;
		private Socket socket;
		
		private string messageToSend;
		private bool messageToSendFlag = false;
		private string messageReceived;
		private bool messageReceivedFlag = false;

		private ASCIIEncoding encoder;

		private bool shouldStop = false;

		public Server()
		{
			encoder = new ASCIIEncoding();
			//this.ipAddress = IPAddress.Parse("192.168.1.1");
			//this.tcpListener = new TcpListener(ipAddress, 8234);
			this.tcpListener = new TcpListener(IPAddress.Any, 8234);

			this.listenThread = new Thread(new ThreadStart(ListenForClient));
			listenThread.Name = "Listen Thread";
			listenThread.Start();
		}

		private void ListenForClient()
		{
			this.tcpListener.Start();
			socket = tcpListener.AcceptSocket();
			socketThread = new Thread(new ParameterizedThreadStart(HandleClientComm));
			socketThread.Name = "Socket Thread";
			socketThread.Start(socket);
		}

		private void HandleClientComm(object tcpSocket)
		{
			Socket socket = (Socket)tcpSocket;
			byte[] message = new byte[4096];
			int bytesRead;

			while(!shouldStop)
			{
				bytesRead = 0;
				try
				{
					bytesRead = socket.Receive(message);
				}
				catch
				{
					continue;
				}
				if(bytesRead != 0)
				{
					messageReceivedFlag = true;
					messageReceived = encoder.GetString(message, 0, bytesRead);
				}
				if(messageToSendFlag)
				{
					byte[] buffer = encoder.GetBytes(messageToSend);
					socket.Send(buffer);
					messageToSendFlag = false;
				}
			}
			socket.Close();
		}

		public string Message
		{
			get
			{
				if (messageReceivedFlag)
					return messageReceived;
				else
					return null;
			}
			set
			{
				messageToSend = value;
				messageToSendFlag = true;
			}
		}

		public void RequestStop()
		{
			shouldStop = true;
		}

		~Server()
		{
			tcpListener.Stop();
			listenThread.Abort();
			socketThread.Abort();
		}
	}
}
