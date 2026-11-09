package br.com.artho.vigialocal;

public final class UpdateVersionTest {
    public static void main(String[] args) {
        check(true,"1.3.1","1.3.0");check(false,"1.3.1","1.3.1");
        check(false,"1.2.9","1.3.0");check(true,"2.0.0","1.99.99");
        check(true,"1.10.0","1.9.99");check(false,"1.3.1-beta","1.3.0");
        check(false,"999999999999.0.0","1.3.0");check(false,null,"1.3.0");
        System.out.println("8 update version checks passed.");
    }
    private static void check(boolean expected,String next,String current) {
        if(UpdateVersion.newer(next,current)!=expected) throw new AssertionError("Bad version comparison: "+next);
    }
}
