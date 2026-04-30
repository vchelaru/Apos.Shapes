namespace Apos.Shapes {
    public readonly struct DashPattern {
        public DashPattern(float dashLength, float gapLength, float phaseOffset = 0f, bool fitToPath = false) {
            DashLength = dashLength;
            GapLength = gapLength;
            PhaseOffset = phaseOffset;
            FitToPath = fitToPath;
        }

        public readonly float DashLength;
        public readonly float GapLength;
        public readonly float PhaseOffset;

        // When true, dash and gap are uniformly scaled so the pattern fits the
        // path length without a seam (closed shapes) or ends on a full dash
        // (lines). When false, the pattern is exact and the last dash is clipped
        // wherever it lands (Skia default).
        public readonly bool FitToPath;

        public float Period => DashLength + GapLength;
    }
}
