using System.Numerics;

namespace PetriNet.Core;

/// <summary>Exact normalized fraction. The default value represents zero.</summary>
public readonly struct Rational : IEquatable<Rational>
{
    public BigInteger Numerator { get; }
    private readonly BigInteger denominator;
    public BigInteger Denominator => denominator.IsZero ? BigInteger.One : denominator;
    public bool IsZero => Numerator.IsZero;

    public Rational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero) throw new DivideByZeroException();
        if (denominator.Sign < 0) (numerator, denominator) = (-numerator, -denominator);
        var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        Numerator = numerator / divisor;
        this.denominator = denominator / divisor;
    }

    public static implicit operator Rational(int value) => new(value, 1);
    public static implicit operator Rational(BigInteger value) => new(value, 1);
    public static Rational operator +(Rational a, Rational b) =>
        new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    public static Rational operator -(Rational a, Rational b) => a + -b;
    public static Rational operator -(Rational a) => new(-a.Numerator, a.Denominator);
    public static Rational operator *(Rational a, Rational b) => new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);
    public static Rational operator /(Rational a, Rational b) => new(a.Numerator * b.Denominator, a.Denominator * b.Numerator);
    public bool Equals(Rational other) => Numerator == other.Numerator && Denominator == other.Denominator;
    public override bool Equals(object? obj) => obj is Rational other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);
    public static bool operator ==(Rational a, Rational b) => a.Equals(b);
    public static bool operator !=(Rational a, Rational b) => !a.Equals(b);
    public override string ToString() => Denominator.IsOne ? Numerator.ToString() : $"{Numerator}/{Denominator}";
}
