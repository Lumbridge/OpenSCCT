struct MitchellCoefficients {
    double inner_constant, inner_quadratic, inner_cubic;
    double outer_constant, outer_linear, outer_quadratic, outer_cubic;
};
extern const double one_sixth;
void initialize_mitchell(double b, double c, struct MitchellCoefficients *p)
{
    double six_c;
    p->inner_constant = one_sixth * (6.0 - (b + b));
    six_c = 6.0 * c;
    p->inner_quadratic = one_sixth * (12.0 * b + six_c - 18.0);
    p->inner_cubic = one_sixth * (12.0 - 9.0 * b - six_c);
    p->outer_constant = one_sixth * (24.0 * c + 8.0 * b);
    p->outer_linear = one_sixth * (-12.0 * b - 48.0 * c);
    p->outer_quadratic = one_sixth * (30.0 * c + 6.0 * b);
    p->outer_cubic = one_sixth * (-b - six_c);
}
