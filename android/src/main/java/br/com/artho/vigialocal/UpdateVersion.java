package br.com.artho.vigialocal;

final class UpdateVersion {
    static boolean newer(String next, String current) {
        if(next==null || current==null || !next.matches("[0-9]{1,3}[.][0-9]{1,3}[.][0-9]{1,3}") ||
            !current.matches("[0-9]{1,3}[.][0-9]{1,3}[.][0-9]{1,3}")) return false;
        String[] a=next.split("[.]"), b=current.split("[.]");
        for(int i=0;i<3;i++) {int comparison=Integer.compare(Integer.parseInt(a[i]),Integer.parseInt(b[i]));if(comparison!=0)return comparison>0;}
        return false;
    }
}
