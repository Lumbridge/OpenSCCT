extern double __cdecl cos(double);
#pragma intrinsic(cos)
extern const double filter_half, filter_one;
extern const double hamming_scale, hamming_offset, blackman_scale, blackman_offset;

double hann_window(double x)
{
    return (cos(3.141592653589793 * x) + filter_one) * filter_half;
}

double hamming_window(double x)
{
    return cos(3.141592653589793 * x) * hamming_scale + hamming_offset;
}

double blackman_window(double x)
{
    return cos(6.283185307179586 * x) * blackman_scale
         + cos(3.141592653589793 * x) * filter_half + blackman_offset;
}



extern double __cdecl sin(double);
extern double __cdecl sqrt(double);
extern double __cdecl exp(double);
#pragma intrinsic(sin, sqrt, exp)
extern const double filter_zero, gaussian_normalization, bessel_origin;
extern double bessel_j1(double);

double sinc_filter(double x)
{
    double angle;
    if (x == filter_zero)
        return filter_one;
    angle = x * 3.141592653589793;
    return sin(angle) / angle;
}

double bessel_filter(double x)
{
    if (x == filter_zero)
        return bessel_origin;
    return bessel_j1(3.141592653589793 * x) / (x + x);
}

double gaussian_filter(double x)
{
    return exp(x * x * -2.0) * sqrt(gaussian_normalization);
}


extern const double minus_one_half, minus_three_halves, three_halves, three_quarters;
double quadratic_filter(double x)
{
    if (x < minus_three_halves)
        return filter_zero;
    if (x < minus_one_half) {
        x += three_halves;
        return filter_half * (x * x);
    }
    if (x < filter_half)
        return three_quarters - x * x;
    if (x < three_halves) {
        x -= three_halves;
        return filter_half * (x * x);
    }
    return filter_zero;
}

extern const double minus_one, minus_two, filter_two, filter_three, filter_four;
extern const double minus_six, filter_six, one_sixth;

double box_filter(double x)
{
    if (x < minus_one_half) return filter_zero;
    if (x < filter_half) return filter_one;
    return filter_zero;
}

double cubic_filter(double x)
{
    if (x < minus_two) return filter_zero;
    if (x < minus_one) {
        x += filter_two;
        return one_sixth * (x * x * x);
    }
    if (x < filter_zero)
        return one_sixth * ((minus_six - filter_three * x) * x * x + filter_four);
    if (x < filter_one)
        return one_sixth * ((filter_three * x - filter_six) * x * x + filter_four);
    if (x < filter_two) {
        x = filter_two - x;
        return one_sixth * (x * x * x);
    }
    return filter_zero;
}


extern const double minus_five, filter_five, filter_eight;

double catrom_filter(double x)
{
    if (x < minus_two) return filter_zero;
    if (x < minus_one)
        return filter_half * (((filter_five + x) * x + filter_eight) * x + filter_four);
    if (x < filter_zero)
        return filter_half * ((minus_five - filter_three * x) * x * x + filter_two);
    if (x < filter_one)
        return filter_half * ((filter_three * x - filter_five) * x * x + filter_two);
    if (x < filter_two)
        return filter_half * (((filter_five - x) * x - filter_eight) * x + filter_four);
    return filter_zero;
}

struct MitchellCoefficients {
    double inner_constant, inner_quadratic, inner_cubic;
    double outer_constant, outer_linear, outer_quadratic, outer_cubic;
};

double mitchell_filter(double x, const struct MitchellCoefficients *p)
{
    if (x < minus_two) return filter_zero;
    if (x < minus_one)
        return p->outer_constant - x * (p->outer_linear - x * (p->outer_quadratic - x * p->outer_cubic));
    if (x < filter_zero)
        return (p->inner_quadratic - x * p->inner_cubic) * x * x + p->inner_constant;
    if (x < filter_one)
        return (p->inner_quadratic + x * p->inner_cubic) * x * x + p->inner_constant;
    if (x < filter_two)
        return p->outer_constant + x * (p->outer_linear + x * (p->outer_quadratic + x * p->outer_cubic));
    return filter_zero;
}
extern const double series_tolerance, filter_quarter;

__declspec(noinline) double bessel_i0(double x)
{
    double sum = filter_one;
    double factor = filter_quarter * (x * x);
    double term = factor;
    int n = 2;
    while (term > series_tolerance) {
        sum += term;
        term = term / (n * n) * factor;
        ++n;
    }
    return sum;
}

struct KaiserParameters { double alpha, normalization; };

double kaiser_window(double x, const struct KaiserParameters *p)
{
    return bessel_i0(sqrt(filter_one - x * x) * p->alpha) * p->normalization;
}

void initialize_kaiser(double alpha, double unused, struct KaiserParameters *p)
{
    p->alpha = alpha;
    p->normalization = filter_one / bessel_i0(alpha);
}
