using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.Util;
using System.Drawing;

namespace Automobin
{
	class GeneralVector
	{
		/*
		 * type: 0 for plane; 1 for space
		*/
		public int type;
	}

	class PlaneVector : GeneralVector
	{
		public MCvPoint2D64f head, tail;

		public PlaneVector(double headX, double headY, double tailX, double tailY)
		{
			this.head = new MCvPoint2D64f(headX, headY);
			this.tail = new MCvPoint2D64f(tailX, tailY);
			this.type = 0;
		}

		public PlaneVector(Point head, Point tail)
		{
			this.head = new MCvPoint2D64f(head.X, head.Y);
			this.head = new MCvPoint2D64f(tail.X, tail.Y);
			this.type = 0;
		}

		public PlaneVector(PointF head, PointF tail)
		{
			this.head = new MCvPoint2D64f(head.X, head.Y);
			this.tail = new MCvPoint2D64f(tail.X, tail.Y);
			this.type = 0;
		}

		public PlaneVector(MCvPoint2D64f head, MCvPoint2D64f tail)
		{
			this.head = head;
			this.tail = tail;
			this.type = 0;
		}

		public double getNorm()
		{
			return Math.Sqrt((head.x - tail.x) * (head.x - tail.x) + (head.y - tail.y) * (head.y - tail.y));
		}
	}

	class SpaceVector : GeneralVector
	{
		public MCvPoint3D64f head, tail;

		public SpaceVector(double headX, double headY, double headZ, double tailX, double tailY, double tailZ)
		{
			this.head = new MCvPoint3D64f(headX, headY, headZ);
			this.tail = new MCvPoint3D64f(tailX, tailY, tailZ);
			this.type = 1;
		}

		public SpaceVector(MCvPoint3D64f head, MCvPoint3D64f tail)
		{
			this.head = head;
			this.tail = tail;
			this.type = 1;
		}

		public double getNorm()
		{
			return Math.Sqrt((head.x - tail.x) * (head.x - tail.x) + (head.y - tail.y) * (head.y - tail.y) + (head.z - tail.z) * (head.z - tail.z));
		}
	}
}
