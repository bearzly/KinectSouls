using Microsoft.Kinect;
using Microsoft.Kinect.Toolkit.Interaction;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace KinectSouls
{
    [Flags]
    enum Button
    {
        DpadUp = 0x1,
        DpadDown = 0x2,
        DpadLeft = 0x4,
        DpadRight = 0x8,
        Start = 0x10,
        Back = 0x20,
        LeftThumb = 0x40,
        RightThumb = 0x80,
        LeftBumper = 0x100,
        RightBumper = 0x200,
        A = 0x1000,
        B = 0x2000,
        X = 0x4000,
        Y = 0x8000
    }

    enum GripEvent
    {
        Grip,
        Ungrip
    }

    enum HandType
    {
        Left,
        Right
    }

    enum Direction
    {
        Left,
        Down,
        Right,
        Up
    }

    class ControllerState
    {
        public Button Buttons { get; set; }
        public int LeftTrigger { get; set; }
        public int RightTrigger { get; set; }
        public int LX { get; set; }
        public int LY { get; set; }
        public int RX { get; set; }
        public int RY { get; set; }

        public override bool Equals(object obj)
        {
            if (obj.GetType() == GetType())
            {
                ControllerState other = (ControllerState)obj;
                return this.Buttons == other.Buttons &&
                    this.LeftTrigger == other.LeftTrigger &&
                    this.RightTrigger == other.RightTrigger &&
                    this.LX == other.LX &&
                    this.LY == other.LY &&
                    this.RX == other.RX &&
                    this.RY == other.RY;
            }
            return base.Equals(obj);
        }

        public override string ToString()
        {
            return String.Format("{{Buttons: {0} LT: {1} RT: {2} LX: {3} LY: {4} RX: {5} RY: {6}}}",
                Buttons, LeftTrigger, RightTrigger, LX, LY, RX, RY);
        }
    }

    abstract class Gesture
    {
        public abstract void handleGrip(GripEvent e, HandType hand);
        public abstract void handleSkeleton(Skeleton s, long timestamp, DrawingContext dc, CoordinateMapper cm);
        public abstract void updateState(ControllerState state);
    }

    abstract class SwipeGesture : Gesture
    {
        const double stableMargin = 0.13;
        const double stableRadiusSquared = stableMargin * stableMargin;
        const double minVelocity = 1.5;
        const long stableTime = 100;
        const long sampleTime = 200;
        const long repeatDelay = 250;
        private readonly Pen arrowPen = new Pen(Brushes.Red, 8.0); 

        enum GestureState
        {
            Start,
            WaitStable,
            WaitMovement,
            TrackMovement,
            Completed,
            WaitRestart
        }

        private GestureState state;
        private HandType hand;
        private long lastTime;
        private SkeletonPoint refPoint;
        private double trackedAngle;
        private double maxDisplacement;
        private SkeletonPoint maxPoint;

        private void transition(GestureState newState)
        {
            if (newState != state)
            {
                //Console.Out.WriteLine("Transition {0} => {1}", state, newState);
                state = newState;
            }
        }

        public SwipeGesture(HandType hand)
        {
            this.hand = hand;
            this.state = GestureState.Start;
        }

        public override void handleGrip(GripEvent e, HandType hand)
        {
            
        }

        private bool isInStableRegion(SkeletonPoint handPoint, SkeletonPoint oldPoint)
        {
            double xOffset = handPoint.X - oldPoint.X;
            double yOffset = handPoint.Y - oldPoint.Y;
            return xOffset * xOffset + yOffset * yOffset < stableRadiusSquared;
        }

        private void drawPath(DrawingContext dc, CoordinateMapper cm)
        {
            const double length = 100;
            DepthImagePoint p = cm.MapSkeletonPointToDepthPoint(refPoint, DepthImageFormat.Resolution640x480Fps30);
            double rads = trackedAngle * Math.PI / 180;
            double dx = length * Math.Cos(rads);
            double dy = -length * Math.Sin(rads);
            Point endPoint = new Point(p.X + dx, p.Y + dy);
            dc.DrawLine(arrowPen, new Point(p.X, p.Y), endPoint);
            dc.DrawEllipse(Brushes.Red, null, endPoint, 10.0, 10.0);
        }

        public override void handleSkeleton(Skeleton s, long timestamp, DrawingContext dc, CoordinateMapper cm)
        {
            if (s == null)
            {
                transition(GestureState.Start);
                return;
            }

            SkeletonPoint handPoint;
            if (hand == HandType.Left)
            {
                handPoint = s.Joints[JointType.HandLeft].Position;

            }
            else
            {
                handPoint = s.Joints[JointType.HandRight].Position;
            }

            if (state == GestureState.Start)
            {
                lastTime = timestamp;
                refPoint = handPoint;
                if (handPoint.Y > s.Joints[JointType.HipCenter].Position.Y)
                {
                    transition(GestureState.WaitStable);
                }
            }
            else if (state == GestureState.WaitStable)
            {
                if (!isInStableRegion(handPoint, refPoint))
                {
                    transition(GestureState.Start);
                }
                else if (timestamp - lastTime > stableTime)
                {
                    transition(GestureState.WaitMovement);
                    refPoint = handPoint;
                }
            }
            else if (state == GestureState.WaitMovement)
            {
                if (!isInStableRegion(handPoint, refPoint))
                {
                    transition(GestureState.TrackMovement);
                    lastTime = timestamp;
                    maxDisplacement = 0;
                }
            }
            else if (state == GestureState.TrackMovement)
            {
                double dx = handPoint.X - refPoint.X;
                double dy = handPoint.Y - refPoint.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);

                if (distance > maxDisplacement)
                {
                    maxDisplacement = distance;
                    maxPoint = handPoint;
                }

                long dt = timestamp - lastTime;
                if (dt > sampleTime)
                {
                    transition(GestureState.Start);

                    double angle = Math.Atan2(maxPoint.Y - refPoint.Y, maxPoint.X - refPoint.X) * 180 / Math.PI;
                    double velocity = maxDisplacement / dt * 1000;
                    if (velocity > minVelocity)
                    {
                        trackedAngle = angle;
                        transition(GestureState.Completed);
                    }
                    else
                    {
                        transition(GestureState.Start);
                    }
                }
            }
            else if (state == GestureState.Completed)
            {
                drawPath(dc, cm);

                double dx = handPoint.X - maxPoint.X;
                double dy = handPoint.Y - maxPoint.Y;
                if (dx * dx + dy * dy > stableRadiusSquared)
                {
                    lastTime = timestamp;
                    transition(GestureState.WaitRestart);
                }
            }
            else if (state == GestureState.WaitRestart)
            {
                drawPath(dc, cm);
                if (timestamp - lastTime > repeatDelay)
                {
                    transition(GestureState.Start);
                }
            }
        }

        protected abstract void handleUp(ControllerState state);
        protected abstract void handleDown(ControllerState state);
        protected abstract void handleLeft(ControllerState state);
        protected abstract void handleRight(ControllerState state);

        public override void updateState(ControllerState c)
        {
            if (state == GestureState.Completed)
            {
                if ((trackedAngle < 45) && (trackedAngle > -45))
                {
                    handleRight(c);
                }
                else if ((trackedAngle < 135) && (trackedAngle > 45))
                {
                    handleUp(c);
                }
                else if ((trackedAngle < 180) && (trackedAngle > 135) || (trackedAngle > -180) && (trackedAngle < -135))
                {
                    handleLeft(c);
                }
                else if ((trackedAngle < -45) && (trackedAngle > -135))
                {
                    handleDown(c);
                }
            }
        }
    }

    class RightHandSwipeGesture : SwipeGesture
    {
        public RightHandSwipeGesture()
            :base(HandType.Right)
        {

        }

        protected override void handleDown(ControllerState state)
        {
            state.Buttons |= Button.B;
        }

        protected override void handleLeft(ControllerState state)
        {
            state.Buttons |= Button.RightBumper;
        }

        protected override void handleRight(ControllerState state)
        {
            state.RightTrigger = Byte.MaxValue;
        }

        protected override void handleUp(ControllerState state)
        {
            state.Buttons |= Button.X;
        }
    }

    class LeftHandSwipeGesture : SwipeGesture
    {
        public LeftHandSwipeGesture()
            : base(HandType.Left)
        {

        }

        protected override void handleDown(ControllerState state)
        {
            state.Buttons |= Button.Y;
        }

        protected override void handleLeft(ControllerState state)
        {
            state.LeftTrigger = Byte.MaxValue;
        }

        protected override void handleRight(ControllerState state)
        {
            state.Buttons |= Button.A;
        }

        protected override void handleUp(ControllerState state)
        {
            state.Buttons |= Button.Start;
        }
    }

    class AnalogStickGesture : Gesture
    {
        private HandType hand;
        private bool isGripped;
        private const double deadzoneRadius = 0.06;
        private const double maxRadius = 0.15;
        private int xValue, yValue;
        private SkeletonPoint gripPoint;
        private SkeletonPoint lastPoint;

        private readonly Brush deadzoneBrush = null;
        private readonly Pen deadzonePen = new Pen(Brushes.Red, 7.0);

        public AnalogStickGesture(HandType hand)
        {
            this.hand = hand;
            this.xValue = 0;
            this.yValue = 0;
        }

        public override void handleGrip(GripEvent e, HandType hand)
        {
            if (hand != this.hand)
            {
                return;
            }
            if (e == GripEvent.Grip)
            {
                this.isGripped = true;
                this.gripPoint = lastPoint;
            }
            else
            {
                this.isGripped = false;
            }
        }

        private Point SkeletonPointToScreen(SkeletonPoint skelpoint, CoordinateMapper cm)
        {
            DepthImagePoint depthPoint = cm.MapSkeletonPointToDepthPoint(skelpoint, DepthImageFormat.Resolution640x480Fps30);
            return new Point(depthPoint.X, depthPoint.Y);
        }

        public override void handleSkeleton(Skeleton s, long timestamp, DrawingContext dc, CoordinateMapper cm)
        {
            if (s == null)
            {
                return;
            }

            if ((s.ClippedEdges & ~FrameEdges.Bottom) != 0)
            {
                this.xValue = 0;
                this.yValue = 0;
                this.isGripped = false;
                return;
            }

            SkeletonPoint handPoint, shoulderPoint;
            if (this.hand == HandType.Left)
            {
                handPoint = s.Joints[JointType.HandLeft].Position;
                shoulderPoint = s.Joints[JointType.ShoulderLeft].Position;
            }
            else
            {
                handPoint = s.Joints[JointType.HandRight].Position;
                shoulderPoint = s.Joints[JointType.ShoulderRight].Position;
            }
            this.lastPoint = handPoint;

            if (!isGripped)
            {
                this.xValue = 0;
                this.yValue = 0;
                return;
            }

            SkeletonPoint deadzonePoint = new SkeletonPoint() { X = gripPoint.X + (float)maxRadius, Y = gripPoint.Y, Z = gripPoint.Z };
            Point center = SkeletonPointToScreen(gripPoint, cm);
            Point deadzoneEdge = SkeletonPointToScreen(deadzonePoint, cm);
            double dx = deadzoneEdge.X - center.X;
            double dy = deadzoneEdge.Y - center.Y;
            double radius = Math.Sqrt(dx * dx + dy * dy);
            dc.DrawEllipse(deadzoneBrush, deadzonePen, center, radius, radius);

            double xOffset = handPoint.X - gripPoint.X;
            double yOffset = handPoint.Y - gripPoint.Y;
            double distance = Math.Sqrt(xOffset * xOffset + yOffset * yOffset);
            if (distance < deadzoneRadius)
            {
                xValue = 0;
                yValue = 0;
            }
            else
            {
                xValue = (int)(short.MaxValue * xOffset / maxRadius * 2);
                if (xValue > 30000 || xValue > short.MaxValue) xValue = short.MaxValue;
                if (xValue < -30000 || xValue < short.MinValue) xValue = short.MinValue;
                yValue = (int)(short.MaxValue * yOffset / maxRadius * 2);
                if (yValue > 20000 || yValue > short.MaxValue) yValue = short.MaxValue;
                if (yValue < -20000 || yValue < short.MinValue) yValue = short.MinValue;

            }
        }

        public override void updateState(ControllerState state)
        {
            if (hand == HandType.Left)
            {
                state.LX = xValue;
                state.LY = yValue;
            }
            else
            {
                state.RX = xValue;
                state.RY = yValue;
            }
        }
    }

    enum TriggerType
    {
        Momentary,
        Toggle,
    }

    class PushGesture : Gesture
    {
        private HandType hand;
        private Button button;
        private TriggerType tt;
        private long startTime;
        private SkeletonPoint lastPoint;
        const double margin = 0.13;
        const double sampleTime = 200;
        const double minZ = 0.03;
        bool trigger;

        public PushGesture(HandType hand, Button b, TriggerType tt)
        {
            this.hand = hand;
            this.button = b;
            this.tt = tt;
            this.trigger = false;
        }

        public override void handleGrip(GripEvent e, HandType hand)
        {
            
        }

        public override void handleSkeleton(Skeleton s, long timestamp, DrawingContext dc, CoordinateMapper cm)
        {
            if (s == null)
            {
                startTime = 0;
                return;
            }

            SkeletonPoint handPoint;
            handPoint = (this.hand == HandType.Left) ? s.Joints[JointType.HandLeft].Position : s.Joints[JointType.HandRight].Position;

            SkeletonPoint hipPoint = s.Joints[JointType.HipCenter].Position;
            if (handPoint.Y < hipPoint.Y)
            {
                startTime = 0;
                return;
            }

            if (startTime == 0)
            {
                lastPoint = handPoint;
                startTime = timestamp;
            }
            else
            {
                double dx = Math.Abs(handPoint.X - lastPoint.X);
                double dy = Math.Abs(handPoint.Y - lastPoint.Y);
                double dz = handPoint.Z - lastPoint.Z;
                if (dx < margin && dy < margin && dz < -minZ)
                {
                    if (timestamp - startTime > sampleTime)
                    {
                        if (this.tt == TriggerType.Momentary)
                        {
                            this.trigger = true;
                        }
                        else
                        {
                            this.trigger = !this.trigger;
                        }
                        this.startTime = 0;
                    }
                }
                else
                {
                    startTime = timestamp;
                    lastPoint = handPoint;
                }
            }
        }

        public override void updateState(ControllerState state)
        {
            if (this.trigger)
            {
                state.Buttons |= this.button;
                if (this.tt == TriggerType.Momentary)
                {
                    this.trigger = false;
                }
            }
        }
    }

    abstract class PoseGesture : Gesture
    {
        enum GestureState
        {
            Idle,
            Holding,
            Recognized,
            Delaying,
            Done,
            Resetting
        }

        private GestureState state;
        private long lastTime;
        const double holdTime = 1000;
        const double delayTime = 800;

        public PoseGesture()
        {
            this.state = GestureState.Idle;
        }

        public override void handleGrip(GripEvent e, HandType hand)
        {

        }

        protected abstract bool isPoseDetected(Skeleton s);
        protected abstract void handleDetected(ControllerState cs);
        protected abstract void handleDone(ControllerState cs);

        public override void handleSkeleton(Skeleton s, long timestamp, DrawingContext dc, CoordinateMapper cm)
        {
            if (s == null)
            {
                state = GestureState.Idle;
                return;
            }

            if (state == GestureState.Idle)
            {
                if (isPoseDetected(s))
                {
                    lastTime = timestamp;
                    state = GestureState.Holding;
                }
            }
            else if (state == GestureState.Holding)
            {
                if (!isPoseDetected(s))
                {
                    state = GestureState.Idle;
                }
                else if (timestamp - lastTime > holdTime)
                {
                    lastTime = timestamp;
                    state = GestureState.Recognized;
                }
            }
            else if (state == GestureState.Recognized)
            {
                state = GestureState.Delaying;
            }
            else if (state == GestureState.Delaying)
            {
                if (timestamp - lastTime > delayTime)
                {
                    state = GestureState.Done;
                }
            }
            else if (state == GestureState.Done)
            {
                state = GestureState.Resetting;
            }
            else if (state == GestureState.Resetting)
            {
                if (!isPoseDetected(s))
                {
                    state = GestureState.Idle;
                }
            }
        }

        public override void updateState(ControllerState c)
        {
            if (state == GestureState.Recognized)
            {
                handleDetected(c);
            }
            else if (state == GestureState.Done)
            {
                handleDone(c);
            }
        }
    }

    class WellWhatIsItGesture : PoseGesture
    {
        private bool isArmStraight(Skeleton s, JointType shoulderType, JointType elbowType, JointType handType)
        {
            const double margin = 0.15;
            double shoulderY = s.Joints[shoulderType].Position.Y;
            double elbowY = s.Joints[elbowType].Position.Y;
            double handY = s.Joints[handType].Position.Y;
            return Math.Abs(shoulderY - elbowY) < margin && Math.Abs(shoulderY - handY) < margin;
        }

        private bool isWellWhatIsIt(Skeleton s)
        {
            return isArmStraight(s, JointType.ShoulderLeft, JointType.ElbowLeft, JointType.HandLeft) &&
                   isArmStraight(s, JointType.ShoulderRight, JointType.ElbowRight, JointType.HandRight);
        }

        protected override bool isPoseDetected(Skeleton s)
        {
            return isWellWhatIsIt(s);
        }

        protected override void handleDetected(ControllerState cs)
        {
            cs.Buttons |= Button.Back;
        }

        protected override void handleDone(ControllerState cs)
        {
            cs.Buttons |= Button.A;
        }
    }

    class PraiseTheSunGesture : PoseGesture
    {
        private double getAngle(Skeleton s, JointType j1, JointType j2)
        {
            SkeletonPoint p1 = s.Joints[j1].Position;
            SkeletonPoint p2 = s.Joints[j2].Position;
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            return Math.Atan2(dy, dx) * 180 / Math.PI;
        }

        private bool isArmAtAngle(Skeleton s, JointType shoulderType, JointType elbowType, JointType handType, double angle, double margin = 15)
        {
            return Math.Abs(getAngle(s, shoulderType, elbowType) - angle) < margin &&
                Math.Abs(getAngle(s, elbowType, handType) - angle) < margin;
        }

        private bool isPraiseTheSun(Skeleton s)
        {
            return isArmAtAngle(s, JointType.ShoulderLeft, JointType.ElbowLeft, JointType.HandLeft, 115) &&
                   isArmAtAngle(s, JointType.ShoulderRight, JointType.ElbowRight, JointType.HandRight, 65);
        }

        protected override bool isPoseDetected(Skeleton s)
        {
            return isPraiseTheSun(s);
        }

        protected override void handleDetected(ControllerState cs)
        {
            cs.Buttons |= Button.Back;
        }

        protected override void handleDone(ControllerState cs)
        {
        }
    }

    class DpadGesture : Gesture
    {
        //duplicates a lot of code from pushgesture

        class HandPushTracker
        {
            HandType hand;
            private long startTime;
            private SkeletonPoint lastPoint;
            const double margin = 0.15;
            const double sampleTime = 200;
            const double minZ = 0.05;

            public HandPushTracker(HandType type)
            {
                this.hand = type;
            }

            public bool Triggered { get; private set; }
            public SkeletonPoint PushPoint { get; private set; }

            public void handleSkeleton(Skeleton s, long timestamp)
            {
                if (s == null)
                {
                    startTime = 0;
                    return;
                }

                SkeletonPoint handPoint;
                handPoint = (this.hand == HandType.Left) ? s.Joints[JointType.HandLeft].Position : s.Joints[JointType.HandRight].Position;
                if (startTime == 0)
                {
                    this.Triggered = false;
                    lastPoint = handPoint;
                    startTime = timestamp;
                }
                else
                {
                    double dx = Math.Abs(handPoint.X - lastPoint.X);
                    double dy = Math.Abs(handPoint.Y - lastPoint.Y);
                    double dz = handPoint.Z - lastPoint.Z;
                    if (dx < margin && dy < margin && dz < -minZ)
                    {
                        if (timestamp - startTime > sampleTime)
                        {
                            this.Triggered = true;
                            this.PushPoint = handPoint;
                            this.startTime = 0;
                        }
                    }
                    else
                    {
                        startTime = timestamp;
                        lastPoint = handPoint;
                    }
                }
            }
        }

        private HandPushTracker leftTracker, rightTracker;
        private Button triggeredButton;
        private const double buttonOffset = 0.35;

        public DpadGesture()
        {
            this.leftTracker = new HandPushTracker(HandType.Left);
            this.rightTracker = new HandPushTracker(HandType.Right);
        }

        public override void handleGrip(GripEvent e, HandType hand)
        {
        }

        public override void handleSkeleton(Skeleton s, long timestamp, DrawingContext dc, CoordinateMapper cm)
        {
            this.triggeredButton = 0;

            this.leftTracker.handleSkeleton(s, timestamp);
            this.rightTracker.handleSkeleton(s, timestamp);
            SkeletonPoint p;
            if (leftTracker.Triggered)
            {
                p = leftTracker.PushPoint;
            }
            else if (rightTracker.Triggered)
            {
                p = rightTracker.PushPoint;
            }
            else
            {
                return;
            }

            SkeletonPoint hipPoint = s.Joints[JointType.HipCenter].Position;
            if (p.Y > hipPoint.Y)
            {
                return;
            }

            double dx = p.X - hipPoint.X;
            if (dx < -buttonOffset)
            {
                this.triggeredButton = Button.DpadLeft;
            }
            else if (dx < 0)
            {
                this.triggeredButton = Button.DpadUp;
            }
            if (dx > buttonOffset)
            {
                this.triggeredButton = Button.DpadRight;
            }
            else if (dx > 0)
            {
                this.triggeredButton = Button.DpadDown;
            }
        }

        public override void updateState(ControllerState state)
        {
            state.Buttons |= this.triggeredButton;
        }
    }

    class KickGesture : Gesture
    {
        private bool trigger;
        private SkeletonPoint lastPoint;
        private long startTime;

        const long sampleTime = 300;
        const double minZ = 0.1;

        public KickGesture()
        {
            trigger = false;
            startTime = 0;
        }

        public override void handleGrip(GripEvent e, HandType hand)
        {
        }

        public override void handleSkeleton(Skeleton s, long timestamp, DrawingContext dc, CoordinateMapper cm)
        {
            if (s == null || s.Joints[JointType.FootRight].TrackingState != JointTrackingState.Tracked)
            {
                trigger = false;
                startTime = 0;
                return;
            }

            SkeletonPoint footPoint = s.Joints[JointType.FootRight].Position;
            if (startTime == 0)
            {
                startTime = timestamp;
                lastPoint = footPoint;
            }
            else
            {
                double dz = footPoint.Y - lastPoint.Y;
                if (dz > minZ)
                {
                    this.trigger = true;
                }
                else
                {
                    startTime = timestamp;
                    lastPoint = footPoint;
                }
            }
        }

        public override void updateState(ControllerState state)
        {
            if (this.trigger)
            {
                this.trigger = false;
                state.LY = short.MaxValue;
                state.Buttons |= Button.RightBumper;
            }
        }
    }

    class VirtualController
    {
        private List<Gesture> gestures;
        private ControllerState currentState;
        private UdpClient socket;

        private const double JointThickness = 3;
        private const double BodyCenterThickness = 10;
        private const double ClipBoundsThickness = 10;
        private readonly Brush centerPointBrush = Brushes.Blue;
        private readonly Brush trackedJointBrush = new SolidColorBrush(Color.FromArgb(255, 68, 192, 68));      
        private readonly Brush inferredJointBrush = Brushes.Yellow;
        private readonly Pen trackedBonePen = new Pen(Brushes.Green, 6);      
        private readonly Pen inferredBonePen = new Pen(Brushes.Gray, 1);

        public DrawingGroup Drawing { get; set; }
        public KinectSensor Sensor { get; set; }

        public VirtualController()
        {
            this.Drawing = null;
            this.Sensor = null;

            this.currentState = new ControllerState();
            this.socket = new UdpClient();
            this.socket.Connect("localhost", 13000);
            this.gestures = new List<Gesture>();
            this.gestures.Add(new RightHandSwipeGesture());
            this.gestures.Add(new LeftHandSwipeGesture());
            this.gestures.Add(new AnalogStickGesture(HandType.Right));
            this.gestures.Add(new AnalogStickGesture(HandType.Left));
            this.gestures.Add(new WellWhatIsItGesture());
            this.gestures.Add(new PraiseTheSunGesture());
            this.gestures.Add(new PushGesture(HandType.Right, Button.RightThumb, TriggerType.Momentary));
            this.gestures.Add(new PushGesture(HandType.Left, Button.LeftBumper, TriggerType.Toggle));
            this.gestures.Add(new DpadGesture());
            this.gestures.Add(new KickGesture());
        }

        public void processSkeletons(Skeleton[] skeletons, long timestamp)
        {
            Skeleton s = skeletons.FirstOrDefault(x => x.TrackingState == SkeletonTrackingState.Tracked);
            ControllerState c = new ControllerState();
            using (DrawingContext dc = this.Drawing.Open())
            {   
                // Draw a transparent background to set the render size
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0.0, 0.0, 640, 480));

                foreach (Gesture g in gestures)
                {
                    g.handleSkeleton(s, timestamp, dc, this.Sensor.CoordinateMapper);
                    g.updateState(c);
                }
                this.processControllerUpdate(c);
                this.drawSkeletons(skeletons, dc);
            }
        }

        public void processInteractionInfo(UserInfo[] infos, long timestamp)
        {
            foreach (var info in infos)
            {
                foreach (var hand in info.HandPointers)
                {
                    if (hand.HandEventType == InteractionHandEventType.None || hand.HandType == InteractionHandType.None)
                    {
                        continue;
                    }
                    GripEvent e = hand.HandEventType == InteractionHandEventType.Grip ? GripEvent.Grip : GripEvent.Ungrip;
                    HandType t = hand.HandType == InteractionHandType.Left ? HandType.Left : HandType.Right;
                    foreach (Gesture g in gestures)
                    {
                        g.handleGrip(e, t);
                    }
                }
            }
        }
        
        private void processControllerUpdate(ControllerState newState)
        {
            if (newState.Equals(currentState))
            {
                return;
            }
            currentState = newState;
            string formatted = String.Format("{0} {1} {2} {3} {4} {5} {6}", (int)currentState.Buttons, currentState.LeftTrigger, currentState.RightTrigger,
                currentState.LX, currentState.LY, currentState.RX, currentState.RY);
            //Console.Out.WriteLine("sending " + formatted);
            Byte[] data = Encoding.ASCII.GetBytes(formatted);
            this.socket.BeginSend(data, data.Length, new AsyncCallback(delegate { }), null);
        }

        private void drawSkeletons(Skeleton[] skeletons, DrawingContext dc)
        {
            if (skeletons.Length != 0)
            {
                foreach (Skeleton skel in skeletons)
                {
                    if (skel.TrackingState == SkeletonTrackingState.Tracked)
                    {
                        this.DrawBonesAndJoints(skel, dc);
                    }
                    else if (skel.TrackingState == SkeletonTrackingState.PositionOnly)
                    {
                        dc.DrawEllipse(
                        this.centerPointBrush,
                        null,
                        this.SkeletonPointToScreen(skel.Position),
                        BodyCenterThickness,
                        BodyCenterThickness);
                    }
                }
            }

            // prevent drawing outside of our render area
            this.Drawing.ClipGeometry = new RectangleGeometry(new Rect(0.0, 0.0, 640, 480));
        }

        /// <summary>
        /// Draws a skeleton's bones and joints
        /// </summary>
        /// <param name="skeleton">skeleton to draw</param>
        /// <param name="drawingContext">drawing context to draw to</param>
        private void DrawBonesAndJoints(Skeleton skeleton, DrawingContext drawingContext)
        {
            // Render Torso
            this.DrawBone(skeleton, drawingContext, JointType.Head, JointType.ShoulderCenter);
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderCenter, JointType.ShoulderLeft);
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderCenter, JointType.ShoulderRight);
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderCenter, JointType.Spine);
            this.DrawBone(skeleton, drawingContext, JointType.Spine, JointType.HipCenter);
            this.DrawBone(skeleton, drawingContext, JointType.HipCenter, JointType.HipLeft);
            this.DrawBone(skeleton, drawingContext, JointType.HipCenter, JointType.HipRight);

            // Left Arm
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderLeft, JointType.ElbowLeft);
            this.DrawBone(skeleton, drawingContext, JointType.ElbowLeft, JointType.WristLeft);
            this.DrawBone(skeleton, drawingContext, JointType.WristLeft, JointType.HandLeft);

            // Right Arm
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderRight, JointType.ElbowRight);
            this.DrawBone(skeleton, drawingContext, JointType.ElbowRight, JointType.WristRight);
            this.DrawBone(skeleton, drawingContext, JointType.WristRight, JointType.HandRight);

            // Left Leg
            this.DrawBone(skeleton, drawingContext, JointType.HipLeft, JointType.KneeLeft);
            this.DrawBone(skeleton, drawingContext, JointType.KneeLeft, JointType.AnkleLeft);
            this.DrawBone(skeleton, drawingContext, JointType.AnkleLeft, JointType.FootLeft);

            // Right Leg
            this.DrawBone(skeleton, drawingContext, JointType.HipRight, JointType.KneeRight);
            this.DrawBone(skeleton, drawingContext, JointType.KneeRight, JointType.AnkleRight);
            this.DrawBone(skeleton, drawingContext, JointType.AnkleRight, JointType.FootRight);

            // Render Joints
            foreach (Joint joint in skeleton.Joints)
            {
                Brush drawBrush = null;

                if (joint.TrackingState == JointTrackingState.Tracked)
                {
                    drawBrush = this.trackedJointBrush;
                }
                else if (joint.TrackingState == JointTrackingState.Inferred)
                {
                    drawBrush = this.inferredJointBrush;
                }

                if (drawBrush != null)
                {
                    drawingContext.DrawEllipse(drawBrush, null, this.SkeletonPointToScreen(joint.Position), JointThickness, JointThickness);
                }
            }
        }

        /// <summary>
        /// Maps a SkeletonPoint to lie within our render space and converts to Point
        /// </summary>
        /// <param name="skelpoint">point to map</param>
        /// <returns>mapped point</returns>
        private Point SkeletonPointToScreen(SkeletonPoint skelpoint)
        {
            // Convert point to depth space.  
            // We are not using depth directly, but we do want the points in our 640x480 output resolution.
            DepthImagePoint depthPoint = this.Sensor.CoordinateMapper.MapSkeletonPointToDepthPoint(skelpoint, DepthImageFormat.Resolution640x480Fps30);
            return new Point(depthPoint.X, depthPoint.Y);
        }

        /// <summary>
        /// Draws a bone line between two joints
        /// </summary>
        /// <param name="skeleton">skeleton to draw bones from</param>
        /// <param name="drawingContext">drawing context to draw to</param>
        /// <param name="jointType0">joint to start drawing from</param>
        /// <param name="jointType1">joint to end drawing at</param>
        private void DrawBone(Skeleton skeleton, DrawingContext drawingContext, JointType jointType0, JointType jointType1)
        {
            Joint joint0 = skeleton.Joints[jointType0];
            Joint joint1 = skeleton.Joints[jointType1];

            // If we can't find either of these joints, exit
            if (joint0.TrackingState == JointTrackingState.NotTracked ||
                joint1.TrackingState == JointTrackingState.NotTracked)
            {
                return;
            }

            // Don't draw if both points are inferred
            if (joint0.TrackingState == JointTrackingState.Inferred &&
                joint1.TrackingState == JointTrackingState.Inferred)
            {
                return;
            }

            // We assume all drawn bones are inferred unless BOTH joints are tracked
            Pen drawPen = this.inferredBonePen;
            if (joint0.TrackingState == JointTrackingState.Tracked && joint1.TrackingState == JointTrackingState.Tracked)
            {
                drawPen = this.trackedBonePen;
            }

            drawingContext.DrawLine(drawPen, this.SkeletonPointToScreen(joint0.Position), this.SkeletonPointToScreen(joint1.Position));
        }
    }
}
