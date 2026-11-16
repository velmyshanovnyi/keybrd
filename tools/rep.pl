#!/usr/bin/perl
# Точна заміна фрагментів у файлі. Вхід (stdin): блоки "old\n=====>\nnew", розділені рядком "#####".
my $file = shift; local $/; open(my $f, '<:raw', $file) or die "no $file"; my $s = <$f>; close $f;
my $crlf = ($s =~ /\r\n/); $s =~ s/\r\n/\n/g;
my $in = <STDIN>; $in =~ s/\r\n/\n/g; my $n = 0;
for my $blk (split /^#####\n/m, $in) {
  my ($old, $new) = split /^=====>\n/m, $blk, 2; die "bad block" unless defined $new;
  chomp $old; chomp $new; $n++;
  my $c = () = $s =~ /\Q$old\E/g;
  die "block $n: found $c times in $file:\n$old\n" unless $c == 1;
  $s =~ s/\Q$old\E/$new/;
}
$s =~ s/\n/\r\n/g if $crlf;
open($f, '>:raw', $file) or die; print $f $s; close $f; print "$file: $n ok\n";
